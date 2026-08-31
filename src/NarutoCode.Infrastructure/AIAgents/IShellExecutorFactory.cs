using Microsoft.Agents.AI.Tools.Shell;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// 会话级 Shell 执行器工厂：按工作目录创建 <see cref="ShellExecutor"/> 并纳入会话跟踪，
/// 会话结束（<see cref="ConversationAgentRuntime"/> 释放）时统一回收底层 Shell 子进程。
/// <para>
/// 与直接 <c>new LocalShellExecutor</c> 的区别：避免业务代码持有未托管资源，
/// 即使创建方遗漏释放，会话释放时也能兜底回收，杜绝 Shell 子进程泄漏。
/// 短生命周期 Shell（如子 Agent 临时 Shell）应在用完后调用 <see cref="ReleaseAsync"/> 归还，
/// 归还时同步移除跟踪引用，避免已释放 Shell 的引用残留到会话结束。
/// </para>
/// </summary>
public interface IShellExecutorFactory
{
    /// <summary>
    /// 按指定工作目录创建会话级 Shell 执行器；Shell 启动后 cwd 锁定在该目录。
    /// </summary>
    /// <param name="workingDirectory">Shell 绑定的工作目录。</param>
    /// <returns>已纳入会话跟踪的 Shell 执行器。</returns>
    ShellExecutor Create(string workingDirectory);

    /// <summary>
    /// 归还并释放 Shell：先从会话跟踪列表移除引用，再关闭底层子进程；
    /// Shell 已不在跟踪列表（重复归还）时直接返回，保证幂等。
    /// </summary>
    /// <param name="shell">由本工厂创建的 Shell 执行器。</param>
    ValueTask ReleaseAsync(ShellExecutor shell);
}
