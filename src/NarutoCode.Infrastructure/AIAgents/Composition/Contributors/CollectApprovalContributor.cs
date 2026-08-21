#pragma warning disable MAAI001
using Microsoft.Agents.AI;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// 审批收集贡献者：挂载审批工具收集提供器，运行时汇总需审批的工具名称。
/// </summary>
public sealed class CollectApprovalContributor : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "CollectApproval";

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        builder.AddAIContextProvider(new CollectApprovalToolAiContextProvider());
    }
}
#pragma warning restore MAAI001
