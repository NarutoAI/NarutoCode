#pragma warning disable MAAI001
using Microsoft.Agents.AI;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;
using NarutoCode.Infrastructure.AIAgents.SubAgents;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// 子 Agent 委派贡献者：挂载当前工作目录可见子 Agent 的委派工具；仅会话级主 Agent 参与，子 Agent 不允许嵌套委派。
/// </summary>
public sealed class SubAgentDelegationContributor(SubAgentRegistry subAgentRegistry) : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "SubAgentDelegation";

    /// <inheritdoc />
    public bool ShouldContribute(AgentCompositionContext context) => context.Profile == AgentProfile.Session;

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        // 递归工厂来自装配上下文：子 Agent 复用同一装配管道（SubAgent 档案）
        builder.AddAIContextProvider(new SubAgentAiContextProvider(
            context.WorkingDirectory,
            subAgentRegistry,
            context.CreateSubAgent));
    }
}
#pragma warning restore MAAI001
