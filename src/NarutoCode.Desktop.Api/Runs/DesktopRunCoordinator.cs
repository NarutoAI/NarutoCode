using System.Collections.Concurrent;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Messages;
using NarutoCode.Domain.Workspaces;
using NarutoCode.Desktop.Api.Workspaces;

namespace NarutoCode.Desktop.Api.Runs;

/// <summary>
/// 内存级 Run 协调器，管理 Run 生命周期、事件通道和 Agent 消息泵。
/// </summary>
internal sealed class DesktopRunCoordinator(
    IConversationService conversationService,
    IConversationRepository conversationRepository,
    DesktopWorkspaceContextAccessor workspaceContextAccessor,
    ILogger<DesktopRunCoordinator> logger) : IDesktopRunCoordinator
{
    // runId → Run
    private readonly ConcurrentDictionary<string, DesktopRun> _runs = new();
    // conversationId → runId（活跃索引）
    private readonly ConcurrentDictionary<long, string> _activeByConversation = new();

    /// <inheritdoc />
    public async Task<DesktopRun> StartAsync(
        ConversationSessionId conversationId,
        AgentMessage message,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");

        // 原子注册会话→Run 映射，已存在则抛异常
        if (!_activeByConversation.TryAdd(conversationId.Value, runId))
        {
            throw new RunAlreadyActiveException(conversationId.Value);
        }

        var run = new DesktopRun(runId, conversationId.Value, cancellationToken);
        _runs[runId] = run;
        run.Status = DesktopRunStatus.Running;

        // 后台泵：消费 Agent 消息流并写入事件通道
        _ = Task.Run(() => PumpAsync(run, conversationId, message), run.RunToken);

        return run;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<RunEvent> ReadEventsAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        if (!_runs.TryGetValue(runId, out var run))
        {
            throw new RunNotFoundException(runId);
        }

        return ReadChannelAsync(run, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ResolveApprovalAsync(
        string runId,
        string approvalId,
        bool approved,
        CancellationToken cancellationToken)
    {
        if (!_runs.TryGetValue(runId, out var run))
        {
            throw new RunNotFoundException(runId);
        }

        // 审批 ID 不匹配时拒绝
        if (run.PendingApprovalId != approvalId)
        {
            throw new InvalidOperationException(
                $"审批 ID 不匹配：期望 {run.PendingApprovalId}，实际 {approvalId}。");
        }

        run.PendingApprovalId = null;
        run.Status = DesktopRunStatus.Running;

        // 构造审批响应消息（1=批准，0=拒绝）
        var responseMessage = new AgentMessage(
            AgentMessageType.ToolApprovalResponse,
            approved ? "1" : "0",
            approvalId);

        // 继续泵送后续 Agent 消息
        await Task.Run(() => PumpAsync(run,
            new ConversationSessionId(run.ConversationId),
            responseMessage), cancellationToken);
    }

    /// <inheritdoc />
    public Task CancelAsync(string runId, CancellationToken cancellationToken)
    {
        if (!_runs.TryGetValue(runId, out var run))
        {
            throw new RunNotFoundException(runId);
        }

        // 发出取消事件
        Emit(run, "run.cancelled");
        run.Status = DesktopRunStatus.Cancelled;
        run.Cancel();

        // 重置运行时会话并清理活跃索引；Run 保留到 SSE 读取端消费完终态事件。
        _activeByConversation.TryRemove(run.ConversationId, out _);

        return conversationService.ResetRuntimeSessionAsync(
            new ConversationSessionId(run.ConversationId), cancellationToken);
    }

    /// <summary>
    /// 后台泵：消费 Agent 消息流，映射为事件写入通道。
    /// </summary>
    private async Task PumpAsync(
        DesktopRun run,
        ConversationSessionId conversationId,
        AgentMessage userMessage)
    {
        try
        {
            var conversation = await conversationRepository.GetByIdAsync(
                conversationId.Value,
                run.RunToken) ?? throw new ConversationNotFoundException(conversationId.Value);

            // 每个 Run 在其自身异步流内使用会话所属工作目录，避免跨项目串目录。
            using var workspaceScope = workspaceContextAccessor.Push(conversation.WorkDirectory);
            await foreach (var agentMessage in conversationService.SendMessageAsync(
                conversationId, userMessage, run.RunToken))
            {
                var eventType = MapEventType(agentMessage.Type);

                // Agent 明确返回错误消息时，Run 进入失败状态
                if (agentMessage.Type == AgentMessageType.Error)
                {
                    run.Status = DesktopRunStatus.Failed;
                }

                // 工具审批请求：暂停 Run 并存储审批 ID
                if (agentMessage.Type == AgentMessageType.ToolApprovalRequest)
                {
                    run.PendingApprovalId = agentMessage.ToolApprovalContent;
                    run.Status = DesktopRunStatus.WaitingApproval;
                }

                Emit(run, eventType, agentMessage,
                    agentMessage.Type == AgentMessageType.ToolApprovalRequest
                        ? agentMessage.ToolApprovalContent
                        : null);
            }

            // 等待审批时保留事件通道和 Run；审批完成后会继续使用同一个 Run 泵送。
            if (run.Status == DesktopRunStatus.WaitingApproval)
            {
                return;
            }

            // 正常结束
            if (run.Status != DesktopRunStatus.Cancelled &&
                run.Status != DesktopRunStatus.Failed)
            {
                run.Status = DesktopRunStatus.Completed;
                Emit(run, "run.completed");
            }
        }
        catch (OperationCanceledException)
        {
            // 取消是正常路径，不做额外处理
        }
        catch (Exception exception)
        {
            run.Status = DesktopRunStatus.Failed;
            Emit(run, "run.failed");
            Log.RunPumpFailed(logger, run.RunId, exception);
        }
        finally
        {
            // 等待审批不是终态，必须保留通道和活跃索引供审批后继续执行。
            if (run.Status != DesktopRunStatus.WaitingApproval)
            {
                run.Writer.TryComplete();
                _activeByConversation.TryRemove(run.ConversationId, out _);
            }
        }
    }

    /// <summary>
    /// 向事件通道写入一条事件。
    /// </summary>
    private void Emit(
        DesktopRun run,
        string eventType,
        AgentMessage? message = null,
        string? approvalId = null)
    {
        var evt = new RunEvent(
            run.RunId,
            run.NextSequence(),
            eventType,
            DateTimeOffset.UtcNow,
            message,
            approvalId);
        if (run.Writer.TryWrite(evt))
        {
            Log.RunEventEmitted(logger, evt.RunId, evt.Sequence, evt.EventType);
        }
    }

    /// <summary>
    /// 将 Agent 消息类型映射为 SSE 事件类型字符串。
    /// </summary>
    private static string MapEventType(AgentMessageType type) => type switch
    {
        AgentMessageType.Content => "message.delta",
        AgentMessageType.Thinking => "thinking.delta",
        AgentMessageType.ToolCall => "tool.started",
        AgentMessageType.Plan => "plan.delta",
        AgentMessageType.RemainingTask => "status.updated",
        AgentMessageType.ToolApprovalRequest => "approval.required",
        AgentMessageType.ToolApprovalResponse => "approval.resolved",
        AgentMessageType.Usage => "usage.updated",
        AgentMessageType.Temporary => "status.updated",
        AgentMessageType.Error => "run.failed",
        _ => "status.updated"
    };

    /// <summary>
    /// 从通道读取事件并转为异步枚举。
    /// </summary>
    private async IAsyncEnumerable<RunEvent> ReadChannelAsync(
        DesktopRun run,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in run.Reader.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }
        }
        finally
        {
            // 读取端退出后才释放 Run，确保短 Run 的终态事件可被稍后建立的 SSE 订阅消费。
            if (run.Status is DesktopRunStatus.Completed or DesktopRunStatus.Cancelled or DesktopRunStatus.Failed)
            {
                _runs.TryRemove(run.RunId, out _);
            }
        }
    }
}
