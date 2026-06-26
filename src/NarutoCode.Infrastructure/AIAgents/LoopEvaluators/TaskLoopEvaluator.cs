using Microsoft.Agents.AI;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;

namespace NarutoCode.Infrastructure.AIAgents.LoopEvaluators;

/// <summary>
/// 基于任务的loop 评估器 检测是否需要继续循环
/// </summary>
#pragma warning disable MAAI001
public class TaskLoopEvaluator : LoopEvaluator
{
    public override ValueTask<LoopEvaluation> EvaluateAsync(LoopContext context,
        CancellationToken cancellationToken = new CancellationToken())
    {
        //是否存在遗留任务
        var isExistsRemainTask = context.Session.IsExistsInProgressTask(context.Agent);
        if (isExistsRemainTask)
        {
            return ValueTask.FromResult(LoopEvaluation.Continue(
                "<system-reminder> Reminder: Continue with existing tasks and use TaskUpdate to keep status current</system-reminder>"));
        }

        return ValueTask.FromResult(LoopEvaluation.Stop());
    }
}
#pragma warning restore MAAI001