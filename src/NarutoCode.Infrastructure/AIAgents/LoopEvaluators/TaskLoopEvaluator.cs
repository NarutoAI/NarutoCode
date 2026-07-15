using Microsoft.Agents.AI;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;

namespace NarutoCode.Infrastructure.AIAgents.LoopEvaluators;

/// <summary>
/// 基于任务的loop 评估器 检测是否需要继续循环
/// </summary>
#pragma warning disable MAAI001
public class TaskLoopEvaluator : LoopEvaluator
{
    public override async ValueTask<LoopEvaluation> EvaluateAsync(LoopContext context,
        CancellationToken cancellationToken = new CancellationToken())
    {
        //如果开启计划或者待办的话 返回允许用户审批
        if (context.Session.IsOpenPlan(context.Agent) || await context.Session.IsOpenTodoAsync(context.Agent))
        {
            return LoopEvaluation.Stop();
        }

        //是否存在遗留任务
        var isExistsRemainTask = context.Session.IsExistsInProgressTask(context.Agent);
        if (isExistsRemainTask)
        {
            return LoopEvaluation.Continue(
                "<system-reminder> Reminder: Continue with the current task and use `TaskUpdate` to keep track of the status. If it is necessary to wait for the user's confirmation, call the `TaskUpdate` tool to update the status to `waiting_ack`</system-reminder>");
        }

        return LoopEvaluation.Stop();
    }
}
#pragma warning restore MAAI001