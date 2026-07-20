using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using NarutoCode.Domain.Messages;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// 工作目录级 Agent 运行时池，按会话隔离 Agent、Session 与持久 Shell。
/// </summary>
internal sealed class WorkspaceAgentPool(
    string workingDirectory,
    Func<ConversationAgentRuntime> createRuntime,
    ILogger<WorkspaceAgentPool> logger) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<long, ConversationAgentRuntime> _runtimes = new();
    private int _activeLeaseCount;
    private int _isEvicted;

    /// <summary>规范化后的工作目录。</summary>
    public string WorkingDirectory { get; } = workingDirectory;

    /// <summary>最近一次访问 UTC 时间。</summary>
    public DateTime LastAccessUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>当前活跃租约数，含等待会话锁的请求。</summary>
    public int ActiveLeaseCount => Volatile.Read(ref _activeLeaseCount);

    /// <summary>池是否已被回收或释放。</summary>
    public bool IsEvicted => Volatile.Read(ref _isEvicted) != 0;

    /// <summary>
    /// 为指定会话获取独占运行时租约。同一会话串行，不同会话可并行。
    /// </summary>
    public async ValueTask<IConversationAgentLease> AcquireAsync(
        ConversationSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        if (IsEvicted)
            throw new WorkspaceAgentPoolEvictedException(WorkingDirectory);

        Interlocked.Increment(ref _activeLeaseCount);
        LastAccessUtc = DateTime.UtcNow;

        try
        {
            var runtime = await GetOrCreateRuntimeAsync(sessionId, cancellationToken);
            LastAccessUtc = DateTime.UtcNow;
            Log.ConversationAgentLeaseAcquired(logger, WorkingDirectory, sessionId.Value, ActiveLeaseCount);
            return new Lease(this, sessionId.Value, runtime);
        }
        catch
        {
            Interlocked.Decrement(ref _activeLeaseCount);
            throw;
        }
    }

    /// <summary>标记指定会话运行时失效，下一次获取时重建。</summary>
    public void InvalidateConversation(ConversationSessionId sessionId)
    {
        if (_runtimes.TryGetValue(sessionId.Value, out var runtime))
        {
            runtime.Invalidate();
            Log.ConversationAgentRuntimeInvalidated(logger, WorkingDirectory, sessionId.Value);
        }
    }

    /// <summary>判断该池是否可被空闲回收。</summary>
    public bool CanEvict(DateTime utcNow, TimeSpan idleTimeout) =>
        !IsEvicted && ActiveLeaseCount == 0 && utcNow - LastAccessUtc >= idleTimeout;

    /// <summary>关闭池内全部会话 Runtime。</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isEvicted, 1) != 0)
            return;

        Log.WorkspaceAgentPoolDisposing(logger, WorkingDirectory, _runtimes.Count, ActiveLeaseCount);

        foreach (var runtime in _runtimes.Values)
            await runtime.DisposeAsync();

        _runtimes.Clear();
    }

    /// <summary>
    /// 获取会话运行时并持有其串行锁；失效实例在持锁后替换，保证同一会话每次仅一个调用方访问。
    /// </summary>
    private async ValueTask<ConversationAgentRuntime> GetOrCreateRuntimeAsync(
        ConversationSessionId sessionId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (IsEvicted)
                throw new WorkspaceAgentPoolEvictedException(WorkingDirectory);

            var runtime = _runtimes.GetOrAdd(sessionId.Value, _ => createRuntime());
            await runtime.SessionGate.WaitAsync(cancellationToken);
            if (!runtime.IsInvalid)
                return runtime;

            runtime.SessionGate.Release();
            if (_runtimes.TryRemove(KeyValuePair.Create(sessionId.Value, runtime)))
            {
                Log.InvalidConversationAgentRuntimeDisposing(logger, WorkingDirectory, sessionId.Value);
                await runtime.DisposeAsync();
            }
        }
    }

    /// <summary>租约释放回调：释放会话锁，降计数。</summary>
    private void OnLeaseDisposed(long sessionId, ConversationAgentRuntime runtime)
    {
        runtime.SessionGate.Release();
        var activeLeaseCount = Interlocked.Decrement(ref _activeLeaseCount);
        LastAccessUtc = DateTime.UtcNow;
        Log.ConversationAgentLeaseReleased(logger, WorkingDirectory, sessionId, activeLeaseCount);
    }

    private sealed class Lease(
        WorkspaceAgentPool pool,
        long sessionId,
        ConversationAgentRuntime runtime) : IConversationAgentLease
    {
        private int _disposed;

        public AIAgent Agent => runtime.Agent;

        public AgentSession? Session
        {
            get => runtime.Session;
            set => runtime.Session = value;
        }

        public void Invalidate() => runtime.Invalidate();

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return ValueTask.CompletedTask;

            pool.OnLeaseDisposed(sessionId, runtime);
            return ValueTask.CompletedTask;
        }
    }
}
