using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using NarutoCode.Domain.Messages;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// 会话 Runtime 缓存：以 <see cref="IMemoryCache"/> 按（工作目录, 会话）键存取 Runtime，
/// 条目注册 <see cref="CancellationChangeToken"/> 过期令牌，缓存条目驱逐后由回调延迟释放 Shell。
/// 同一会话经 SessionGate 串行，跨会话并行；失效实例在持锁校验后销毁重建。
/// </summary>
public sealed class ConversationRuntimeCache(ILogger<ConversationRuntimeCache> logger) : IAsyncDisposable
{
    // 显式字段：嵌套类（租约/驱逐回调）需要跨类型访问日志器
    private readonly ILogger<ConversationRuntimeCache> _logger = logger;

    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    // 每个缓存条目的过期令牌源，整体释放时统一取消以驱逐全部条目
    private readonly ConcurrentDictionary<(string WorkingDirectory, long SessionId), CancellationTokenSource> _expirationTokens = new();

    // 创建/驱逐互斥锁：保证同一会话仅创建一个 Runtime，且驱逐时实例一致性校验无竞态
    private readonly Lock _createLock = new();
    private int _disposed;

    /// <summary>当前缓存的会话 Runtime 数量。</summary>
    public int RuntimeCount => _expirationTokens.Count;

    /// <summary>
    /// 获取或创建会话 Runtime 并持会话锁后返回租约；同一会话串行，不同会话并行。
    /// </summary>
    /// <param name="workingDirectory">规范化后的工作目录。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="createRuntime">创建新 Runtime 的工厂（含持久 Shell 与 Agent）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>覆盖完整 Agent Run 生命周期的会话租约。</returns>
    public async ValueTask<IConversationAgentLease> AcquireAsync(
        string workingDirectory,
        ConversationSessionId sessionId,
        Func<ConversationAgentRuntime> createRuntime,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var runtime = GetOrCreateRuntime(workingDirectory, sessionId, createRuntime);

            // 同一会话串行：等待并持有会话锁后才返回租约；失效实例也要先等旧租约释放，保持串行语义
            await runtime.SessionGate.WaitAsync(cancellationToken);
            if (!runtime.IsInvalid)
            {
                Log.ConversationAgentLeaseAcquired(_logger, workingDirectory, sessionId.Value);
                return new Lease(this, workingDirectory, sessionId.Value, runtime);
            }

            // 失效实例：此刻会话锁已空闲（旧租约已释放），安全驱逐缓存条目并释放 Shell 后重建
            runtime.SessionGate.Release();
            await RemoveAsync(workingDirectory, sessionId, runtime);
        }
    }

    /// <summary>
    /// 标记会话 Runtime 失效，下一次获取时在旧租约释放后销毁并重建；缓存条目暂不驱逐以保证串行等待。
    /// </summary>
    /// <param name="workingDirectory">规范化后的工作目录。</param>
    /// <param name="sessionId">会话标识。</param>
    public void Invalidate(string workingDirectory, ConversationSessionId sessionId)
    {
        if (_cache.TryGetValue(CreateKey(workingDirectory, sessionId), out ConversationAgentRuntime? runtime) &&
            runtime is not null)
        {
            runtime.Invalidate();
            Log.ConversationAgentRuntimeInvalidated(_logger, workingDirectory, sessionId.Value);
        }
        else
        {
            Log.ConversationRuntimeNotFoundForReset(_logger, workingDirectory, sessionId.Value);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        // 取消全部过期令牌驱逐缓存条目，驱逐回调后台等待会话锁空闲后释放 Shell
        foreach (var cts in _expirationTokens.Values)
        {
            TryCancel(cts);
        }

        _expirationTokens.Clear();
        _cache.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 获取或创建 Runtime：创建路径加锁双检，保证同一会话仅创建一个 Runtime（含一个持久 Shell 进程）。
    /// </summary>
    private ConversationAgentRuntime GetOrCreateRuntime(
        string workingDirectory, ConversationSessionId sessionId, Func<ConversationAgentRuntime> createRuntime)
    {
        var key = CreateKey(workingDirectory, sessionId);
        lock (_createLock)
        {
            // 双检：命中（含已失效未重建的）直接复用，由 AcquireAsync 循环负责失效重建；未命中才创建
            if (_cache.TryGetValue(key, out ConversationAgentRuntime? existing))
            {
                return existing;
            }

            var runtime = createRuntime();
            var cts = new CancellationTokenSource();
            _expirationTokens[key] = cts;
            using var entry = _cache.CreateEntry(key);
            entry.Value = runtime;
            // IChangeToken 过期机制：令牌取消即驱逐缓存条目（整体释放时触发）
            entry.AddExpirationToken(new CancellationChangeToken(cts.Token));
            // 驱逐回调：后台等待会话锁空闲后释放 Shell，避免打断执行中的流
            entry.RegisterPostEvictionCallback(OnRuntimeEvicted, this);
            return runtime;
        }
    }

    /// <summary>
    /// 驱逐失效 Runtime：持创建锁做实例一致性校验后移除缓存条目并同步释放 Shell。
    /// 若条目已被其它调用方替换为新实例则跳过，避免误释放他人 Runtime。
    /// </summary>
    private async ValueTask RemoveAsync(
        string workingDirectory, ConversationSessionId sessionId, ConversationAgentRuntime runtime)
    {
        Log.InvalidConversationAgentRuntimeDisposing(_logger, workingDirectory, sessionId.Value);

        lock (_createLock)
        {
            // 实例一致性校验：仅当缓存仍指向当前失效实例时才驱逐，防止并发重建后误删新 Runtime
            if (!_cache.TryGetValue(CreateKey(workingDirectory, sessionId), out var current) ||
                !ReferenceEquals(current, runtime))
            {
                return;
            }

            // 移除缓存条目触发驱逐回调；回调的后台释放与下方同步释放均经 Runtime 幂等保护
            _cache.Remove(CreateKey(workingDirectory, sessionId));
        }

        _expirationTokens.TryRemove(CreateKey(workingDirectory, sessionId), out _);
        await runtime.DisposeAsync();
    }

    /// <summary>
    /// 缓存驱逐回调：后台等待会话锁空闲后释放 Shell，保证执行中的流不被打断。
    /// </summary>
    private static void OnRuntimeEvicted(object key, object? value, EvictionReason reason, object? state)
    {
        if (value is not ConversationAgentRuntime runtime || state is not ConversationRuntimeCache self)
        {
            return;
        }

        var (workingDirectory, sessionId) = ((string WorkingDirectory, long SessionId))key;
        Log.ConversationRuntimeEvicted(self._logger, workingDirectory, sessionId, reason);

        // 后台延迟释放：等待当前租约结束（会话锁空闲）后再关闭 Shell
        _ = Task.Run(async () =>
        {
            try
            {
                await runtime.SessionGate.WaitAsync();
                runtime.SessionGate.Release();
                await runtime.DisposeAsync();
            }
            catch (Exception exception)
            {
                Log.ConversationRuntimeDisposalFailed(self._logger, exception, workingDirectory, sessionId);
            }
        });
    }

    /// <summary>
    /// 构造缓存复合键：（工作目录, 会话标识）。
    /// </summary>
    private static (string WorkingDirectory, long SessionId) CreateKey(
        string workingDirectory, ConversationSessionId sessionId) => (workingDirectory, sessionId.Value);

    /// <summary>
    /// 取消过期令牌，令牌已释放时静默忽略（驱逐由缓存条目移除兜底）。
    /// </summary>
    private static void TryCancel(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 忽略：令牌已释放
        }
    }

    /// <summary>
    /// 会话租约：释放时归还会话锁。
    /// </summary>
    private sealed class Lease(
        ConversationRuntimeCache cache,
        string workingDirectory,
        long sessionId,
        ConversationAgentRuntime runtime) : IConversationAgentLease
    {
        private int _disposed;

        /// <inheritdoc />
        public AIAgent Agent => runtime.Agent;

        /// <inheritdoc />
        public AgentSession? Session
        {
            get => runtime.Session;
            set => runtime.Session = value;
        }

        /// <inheritdoc />
        public void Invalidate() => runtime.Invalidate();

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            runtime.SessionGate.Release();
            Log.ConversationAgentLeaseReleased(cache._logger, workingDirectory, sessionId);
            return ValueTask.CompletedTask;
        }
    }
}
