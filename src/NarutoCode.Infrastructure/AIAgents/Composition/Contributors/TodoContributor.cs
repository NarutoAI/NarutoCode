#pragma warning disable MAAI001
using Microsoft.Agents.AI;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// 待办贡献者：挂载待办任务管理提供器，经工具延续回合跳过包装保护工具调用协议相邻性。
/// </summary>
public sealed class TodoContributor : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "Todo";

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        // Wrap：工具审批/工具结果回合跳过上下文注入，避免破坏工具调用协议要求的消息相邻性
        builder.AddAIContextProvider(ToolContinuationSkippingAiContextProvider.Wrap(new TodoProvider()));
    }
}
#pragma warning restore MAAI001
