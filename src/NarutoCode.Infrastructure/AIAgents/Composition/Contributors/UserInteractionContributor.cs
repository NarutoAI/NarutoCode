#pragma warning disable MAAI001
using Microsoft.Agents.AI;
using NarutoCode.Application.Interactions;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// 用户交互贡献者：挂载 ask_user 结构化交互工具；仅 CLI 宿主开启开关且会话级主 Agent 参与。
/// </summary>
public sealed class UserInteractionContributor(
    IUserInteractionManager userInteractionManager,
    AgentFactoryOptions agentFactoryOptions) : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "UserInteraction";

    /// <inheritdoc />
    public bool ShouldContribute(AgentCompositionContext context) =>
        // 双条件：宿主开关（仅 CLI 为 true）+ 会话级档案（子 Agent 不直接面向用户）
        agentFactoryOptions.EnableUserInteractionTools && context.Profile == AgentProfile.Session;

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        builder.AddAIContextProvider(new AskUserInteractionProvider(userInteractionManager));
    }
}
#pragma warning restore MAAI001
