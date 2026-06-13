using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Messages;

namespace NarutoCodeCli.Ui;

/// <summary>
/// CLI 层会话视图状态，负责保存当前画布需要展示的消息。
/// </summary>
internal sealed class ChatSessionState
{
    private readonly List<ChatMessage> messages = [];
    private long initialContextTokenUsage;

    /// <summary>
    /// 当前会话消息列表。
    /// </summary>
    public IReadOnlyList<ChatMessage> Messages => messages;

    /// <summary>
    /// 当前待审批的工具调用请求。
    /// </summary>
    public AgentMessage? PendingToolApprovalRequest { get; private set; }

    /// <summary>
    /// 当前是否正在等待用户审批工具调用。
    /// </summary>
    public bool IsToolApprovalPending => PendingToolApprovalRequest is not null;

    /// <summary>
    /// 当前是否存在正在运行的 Agent 请求。
    /// </summary>
    public bool IsOperationRunning { get; private set; }

    /// <summary>
    /// 基于模型 Usage 消息累计的上下文 Token 使用量，用于 TUI 底部状态展示。
    /// </summary>
    public long ContextTokenUsage => initialContextTokenUsage + messages.Sum(message => message.ContextTokenUsage);

    /// <summary>
    /// 使用历史消息恢复当前 UI 状态。
    /// </summary>
    /// <param name="history">当前工作目录对应的历史消息。</param>
    public void LoadHistory(ConversationHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        messages.Clear();
        initialContextTokenUsage = Math.Max(0, history.TokenCount);
        PendingToolApprovalRequest = null;
        IsOperationRunning = false;
        var totalMessages = history.Messages.Count;
        foreach (var (index, item) in history.Messages.Index())
        {
            if (item.Role == ConversationMessageRole.user)
            {
                AddUserHistoryMessage(item.Message);
                continue;
            }

            AddAssistantHistoryMessage(item.Message, index + 1 == totalMessages);
        }
    }

    /// <summary>
    /// 添加一条消息。
    /// </summary>
    /// <param name="message">需要添加的消息。</param>
    public void AddMessage(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        messages.Add(message);
    }

    /// <summary>
    /// 创建当前用户输入对应的领域消息。
    /// </summary>
    /// <param name="input">用户输入。</param>
    /// <returns>普通用户消息或工具审批响应消息。</returns>
    public AgentMessage CreateOutgoingMessage(string input)
    {
        if (PendingToolApprovalRequest is not { } approvalRequest)
        {
            return new AgentMessage(AgentMessageType.Content, input);
        }

        return new AgentMessage(
            AgentMessageType.ToolApprovalResponse,
            input.Trim(),
            approvalRequest.ToolApprovalContent);
    }

    /// <summary>
    /// 标记当前存在正在运行的 Agent 请求。
    /// </summary>
    public void MarkOperationRunning()
    {
        IsOperationRunning = true;
    }

    /// <summary>
    /// 标记当前 Agent 请求已经结束。
    /// </summary>
    public void MarkOperationCompleted()
    {
        IsOperationRunning = false;
    }

    /// <summary>
    /// 完成指定工具审批流程；如果期间产生了新的审批请求，则保留新的审批状态。
    /// </summary>
    /// <param name="toolApprovalContent">本次已响应的工具审批上下文。</param>
    public void CompleteToolApproval(string toolApprovalContent)
    {
        if (PendingToolApprovalRequest?.ToolApprovalContent == toolApprovalContent)
        {
            PendingToolApprovalRequest = null;
        }
    }

    /// <summary>
    /// 标记当前会话正在等待工具审批。
    /// </summary>
    /// <param name="request">工具审批请求消息。</param>
    public void MarkToolApprovalPending(AgentMessage request)
    {
        if (request.Type != AgentMessageType.ToolApprovalRequest)
        {
            throw new ArgumentException("只有工具审批请求消息才能进入审批等待状态。", nameof(request));
        }

        PendingToolApprovalRequest = request;
    }

    private void AddUserHistoryMessage(AgentMessage message)
    {
        messages.Add(ChatMessage.CreateUser(message.Content));

        if (message.Type == AgentMessageType.ToolApprovalResponse)
        {
            PendingToolApprovalRequest = null;
        }
    }

    private void AddAssistantHistoryMessage(AgentMessage message, bool isLast)
    {
        if (message.Type == AgentMessageType.Usage)
        {
            return;
        }

        var assistantMessage = messages.Count > 0 && messages[^1].Role == ChatRole.Assistant
            ? messages[^1]
            : ChatMessage.CreateAssistant();

        if (messages.Count == 0 || messages[^1].Role != ChatRole.Assistant)
        {
            messages.Add(assistantMessage);
        }

        assistantMessage.Append(message);

        if (isLast && message.Type == AgentMessageType.ToolApprovalRequest)
        {
            PendingToolApprovalRequest = message;
        }
    }
}
