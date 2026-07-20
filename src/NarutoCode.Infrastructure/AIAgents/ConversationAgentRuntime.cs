using Microsoft.Agents.AI;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// 会话专属 Agent、Session 与持久 Shell 的运行时容器。
/// </summary>
internal sealed class ConversationAgentRuntime(
    AIAgent agent,
    IAsyncDisposable persistentShell) : IAsyncDisposable
{
    private int _isInvalid;
    private int _isDisposed;

    /// <summary>
    /// 会话专属 Agent。
    /// </summary>
    public AIAgent Agent { get; } = agent;

    /// <summary>
    /// 会话恢复后的 Agent Session。
    /// </summary>
    public AgentSession? Session { get; set; }

    /// <summary>
    /// 同一会话 Run 的串行锁。
    /// </summary>
    public SemaphoreSlim SessionGate { get; } = new(1, 1);

    /// <summary>
    /// 当前运行时是否已被标记失效，下一次获取时将重建。
    /// </summary>
    public bool IsInvalid => Volatile.Read(ref _isInvalid) != 0;

    /// <summary>
    /// 标记运行时失效，当前租约释放后将被销毁并重建。
    /// </summary>
    public void Invalidate() => Interlocked.Exchange(ref _isInvalid, 1);

    /// <summary>
    /// 释放持久 Shell。会话锁保留至池关闭，避免正在等待的调用方访问已释放的信号量。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return ValueTask.CompletedTask;

        return persistentShell.DisposeAsync();
    }
}
