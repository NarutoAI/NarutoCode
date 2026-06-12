namespace NarutoCode.Domain.Messages;

/// <summary>
/// Agent 对话消息实体，用于表达普通内容、思考、工具调用、工具审批和多模态用户输入消息。
/// </summary>
public readonly record struct AgentMessage
{
    /// <summary>
    /// 创建 Agent 对话消息。
    /// </summary>
    /// <param name="type">消息内容类型。</param>
    /// <param name="content">消息内容。</param>
    /// <param name="toolApprovalContent">工具调用标识，工具审批请求和审批响应必须提供。</param>
    /// <param name="createdAt">可选的创建时间，未传入时使用当前本地时间。</param>
    /// <param name="attachments">用户消息附件集合，用于携带图片等多模态输入。</param>
    /// <exception cref="ArgumentException">当工具审批消息缺少 CallId，或审批响应不是 1/0 时抛出。</exception>
    public AgentMessage(
        AgentMessageType type,
        string content,
        string toolApprovalContent = "",
        DateTimeOffset? createdAt = null,
        IReadOnlyList<AgentMessageAttachment>? attachments = null,bool isAutoSend=false)
    {
        if (RequiresToolApproval(type) && string.IsNullOrWhiteSpace(toolApprovalContent))
        {
            throw new ArgumentException("工具审批消息必须包含 toolApprovalContent。", nameof(toolApprovalContent));
        }

        if (type == AgentMessageType.ToolApprovalResponse && content.Trim() is not ("1" or "0"))
        {
            throw new ArgumentException("工具审批响应只能是 1 或 0。", nameof(content));
        }

        Type = type;
        Content = content;
        ToolApprovalContent = toolApprovalContent;
        CreatedAt = createdAt ?? DateTimeOffset.Now;
        Attachments = attachments is { Count: > 0 }
            ? attachments.ToArray()
            : [];
        IsAutoSend = isAutoSend;
    }

    /// <summary>
    /// 消息内容类型。
    /// </summary>
    public AgentMessageType Type { get; }

    /// <summary>
    /// 消息内容。
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// 工具调用标识，用于关联工具审批请求和审批响应。
    /// </summary>
    public string ToolApprovalContent { get; }

    /// <summary>
    /// 消息创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// 用户消息附件集合，用于携带图片等多模态输入。
    /// </summary>
    public IReadOnlyList<AgentMessageAttachment> Attachments { get; }

    /// <summary>
    /// 是否自动发送的
    /// </summary>
    public bool IsAutoSend { get; }

    private static bool RequiresToolApproval(AgentMessageType type)
    {
        return type is AgentMessageType.ToolApprovalRequest or AgentMessageType.ToolApprovalResponse;
    }
}
