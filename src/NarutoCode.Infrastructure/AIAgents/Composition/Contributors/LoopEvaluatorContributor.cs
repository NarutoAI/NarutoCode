#pragma warning disable MAAI001
using Microsoft.Agents.AI;
using NarutoCode.Infrastructure.AIAgents.LoopEvaluators;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// 循环评估器贡献者：挂载待办完成与任务循环评估器，控制 Agent Run 的继续/停止判定。
/// </summary>
public sealed class LoopEvaluatorContributor : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "LoopEvaluator";

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        // 待办完成评估在前，任务循环评估在后
        builder.AddLoopEvaluator(new TodoCompletionLoopEvaluator(
            new TodoCompletionLoopEvaluatorOptions { Modes = ["execute"] }));
        builder.AddLoopEvaluator(new TaskLoopEvaluator());
    }
}
#pragma warning restore MAAI001
