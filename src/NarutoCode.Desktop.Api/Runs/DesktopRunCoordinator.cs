using System.Collections.Concurrent;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Messages;
using NarutoCode.Domain.Workspaces;

namespace NarutoCode.Desktop.Api.Runs;

/// <summary>
/// 内存级 Run 协调器，管理 Run 生命周期、事件通道和 Agent 消息泵。
/// </summary>
internal sealed class DesktopRunCoordinator(
    IConversationService conversationService,
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

        // 重置运行时会话并清理索引
        _activeByConversation.TryRemove(run.ConversationId, out _);
        _runs.TryRemove(runId, out _);

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
            // 无论成功失败，都完成通道并清理活跃索引
            run.Writer.TryComplete();
            _activeByConversation.TryRemove(run.ConversationId, out _);
            _runs.TryRemove(run.RunId, out _);
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
        run.Writer.TryWrite(evt);
    }

    /// <summary>
    /// 将 Agent 消息类型映射为 SSE 事件类型字符串。
    /// </summary>
    private static string MapEventType(AgentMessageType type) => type switch
    {
        AgentMessageType.Content => "message.delta",
        AgentMessageType.Thinking => "thinking.delta",
        AgentMessageType.ToolCall => "tool.started",
        AgentMessageType.ToolApprovalRequest => "approval.required",
        AgentMessageType.Error => "run.failed",
        _ => "message.delta"
    };

    /// <summary>
    /// 从通道读取事件并转为异步枚举。
    /// </summary>
    private static async IAsyncEnumerable<RunEvent> ReadChannelAsync(
        DesktopRun run,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in run.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }
}
