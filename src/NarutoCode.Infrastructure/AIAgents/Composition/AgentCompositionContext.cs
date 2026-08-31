using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;

namespace NarutoCode.Infrastructure.AIAgents.Composition;

/// <summary>
/// Agent 装配上下文：每次创建 Agent 时构建，携带工作目录、持久 Shell、会话 Shell 工厂、身份档案与子 Agent 工厂，
/// 供各贡献者按现场参数构建编排要素。
/// </summary>
/// <param name="WorkingDirectory">当前 Agent 绑定的工作目录。</param>
/// <param name="PersistentShell">当前 Agent 专属的持久 Shell。</param>
/// <param name="ShellFactory">当前会话的 Shell 执行器工厂，跟踪会话内全部 Shell 并随会话统一释放。</param>
/// <param name="Profile">当前 Agent 的身份档案，决定贡献者参与规则。</param>
/// <param name="CreateSubAgent">按目标工作目录创建子 Agent（SubAgent 档案）的工厂委托，供委派能力使用。</param>
public sealed class AgentCompositionContext(
    string workingDirectory,
    ShellExecutor persistentShell,
    IShellExecutorFactory shellFactory,
    AgentProfile profile,
    Func<string, ShellExecutor, AIAgent> createSubAgent)
{
    /// <summary>当前 Agent 绑定的工作目录。</summary>
    public string WorkingDirectory { get; } = workingDirectory;

    /// <summary>当前 Agent 专属的持久 Shell。</summary>
    public ShellExecutor PersistentShell { get; } = persistentShell;

    /// <summary>当前会话的 Shell 执行器工厂，跟踪会话内全部 Shell 并随会话统一释放。</summary>
    public IShellExecutorFactory ShellFactory { get; } = shellFactory;

    /// <summary>当前 Agent 的身份档案，决定贡献者参与规则。</summary>
    public AgentProfile Profile { get; } = profile;

    /// <summary>按目标工作目录创建子 Agent（SubAgent 档案）的工厂委托，供委派能力使用。</summary>
    public Func<string, ShellExecutor, AIAgent> CreateSubAgent { get; } = createSubAgent;
}
