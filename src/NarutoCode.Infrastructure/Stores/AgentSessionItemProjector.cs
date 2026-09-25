using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Messages;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// agent_session_items 载荷到 TUI 历史消息契约的投影（纯映射，无数据库访问）：
/// payload 按 kind 反序列化为专属结构后还原为 <see cref="ConversationHistoryMessage" />，
/// 供 UI 历史加载复用。
/// </summary>
internal static class AgentSessionItemProjector
{
    /// <summary>
    /// 将一条 Item 记录投影为 TUI 历史消息。
    /// 用户交互（含等待态）渲染为问答卡片；消息类 Item 仅完成态（succeeded/failed）渲染；
    /// 其余 kind 与非法载荷返回 <see langword="null" />（不阻断整体历史渲染）。
    /// </summary>
    /// <param name="kind">Item 类型，取值见 <see cref="ConversationItemKinds" />。</param>
    /// <param name="status">Item 状态，取值见 <see cref="ConversationItemStatuses" />。</param>
    /// <param name="payload">Item 载荷 JSON。</param>
    /// <param name="createdAt">Item 落库时间。</param>
    /// <returns>历史消息；不需要渲染时返回 <see langword="null" />。</returns>
    internal static ConversationHistoryMessage? Project(
        string kind,
        string status,
        string payload,
        DateTime createdAt)
    {
        // 用户交互问答卡片：payload 为 { request, result } 复合结构（pending 态也渲染，仅展示问题）
        if (string.Equals(kind, ConversationItemKinds.UserInteraction, StringComparison.Ordinal))
        {
            return ProjectUserInteraction(payload);
        }

        // 消息类 Item 仅完成态渲染（用户输入 / 输出 / 思考 / 工具调用均为完成态落库）
        var isCompleted = string.Equals(status, ConversationItemStatuses.Succeeded, StringComparison.Ordinal)
                          || string.Equals(status, ConversationItemStatuses.Failed, StringComparison.Ordinal);
        if (!isCompleted)
        {
            return null;
        }

        // 按 kind 反序列化专属 payload，映射为 TUI 契约（角色 / 消息类型 / 展示文本）
        var createdAtOffset = new DateTimeOffset(createdAt);
        return kind switch
        {
            ConversationItemKinds.UserMessage => ProjectUserMessage(payload, createdAtOffset),
            ConversationItemKinds.AgentMessage => ProjectText(
                ConversationItemJsonSerializerContext.DeserializePayload<AgentMessageItemPayload>(payload)?.Text,
                ConversationMessageRole.assistant,
                AgentMessageType.Content,
                createdAtOffset),
            ConversationItemKinds.Reasoning => ProjectText(
                ConversationItemJsonSerializerContext.DeserializePayload<ReasoningItemPayload>(payload)?.Text,
                ConversationMessageRole.assistant,
                AgentMessageType.Thinking,
                createdAtOffset),
            ConversationItemKinds.ToolCall => ProjectToolCall(payload, createdAtOffset),
            ConversationItemKinds.ToolApprovalRequest => ProjectToolApprovalRequest(payload, createdAtOffset),
            ConversationItemKinds.Error => ProjectText(
                ConversationItemJsonSerializerContext.DeserializePayload<ErrorItemPayload>(payload)?.Message,
                ConversationMessageRole.assistant,
                AgentMessageType.Error,
                createdAtOffset),
            _ => null
        };
    }

    /// <summary>
    /// 从 userMessage 载荷中提取预览文本（会话列表用）。
    /// </summary>
    /// <param name="payload">Item 载荷 JSON。</param>
    /// <returns>预览文本；无法解析时返回空字符串。</returns>
    internal static string ReadUserMessagePreview(string payload)
    {
        return ConversationItemJsonSerializerContext
            .DeserializePayload<UserMessageItemPayload>(payload)?.Text ?? string.Empty;
    }

    /// <summary>
    /// 投影用户交互 Item：等待态仅显示问题，终态追加回答摘要，不泄露内部工具名。
    /// </summary>
    /// <param name="payload">Item 载荷 JSON。</param>
    /// <returns>历史消息；载荷非法时返回 <see langword="null" />。</returns>
    private static ConversationHistoryMessage? ProjectUserInteraction(string payload)
    {
        var interaction = UserInteractionJsonSerializerContext.DeserializeItemPayload(payload);
        if (interaction is null)
        {
            return null;
        }

        var question = interaction.Request.Question;
        var content = interaction.Result is null
            ? $"❓ {question}"
            : $"❓ {question}{Environment.NewLine}↳ {interaction.Result.Value}";
        return new ConversationHistoryMessage(
            ConversationMessageRole.assistant,
            new AgentMessage(AgentMessageType.Content, content, createdAt: DateTimeOffset.Now));
    }

    /// <summary>
    /// 投影 userMessage Item：真实用户输入文本 + 图片附件还原为多模态 user 消息。
    /// </summary>
    /// <param name="payload">Item 载荷 JSON。</param>
    /// <param name="createdAt">落库时间。</param>
    /// <returns>历史消息；载荷非法时返回 <see langword="null" />。</returns>
    private static ConversationHistoryMessage? ProjectUserMessage(string payload, DateTimeOffset createdAt)
    {
        var userPayload = ConversationItemJsonSerializerContext.DeserializePayload<UserMessageItemPayload>(payload);
        if (userPayload is null)
        {
            return null;
        }

        // 附件以原始字节持久化，重载后直接还原为多模态消息
        var attachments = userPayload.Attachments is { Count: > 0 }
            ? userPayload.Attachments
                .Select(a => new AgentMessageAttachment(a.Data, a.MediaType))
                .ToArray()
            : null;
        return new ConversationHistoryMessage(
            ConversationMessageRole.user,
            new AgentMessage(AgentMessageType.Content, userPayload.Text, attachments: attachments, createdAt: createdAt));
    }

    /// <summary>
    /// 投影纯文本类 Item（agentMessage / reasoning / error）。
    /// </summary>
    /// <param name="text">载荷文本；为 <see langword="null" /> 表示载荷非法。</param>
    /// <param name="role">消息角色。</param>
    /// <param name="messageType">TUI 消息类型。</param>
    /// <param name="createdAt">落库时间。</param>
    /// <returns>历史消息；载荷非法时返回 <see langword="null" />。</returns>
    private static ConversationHistoryMessage? ProjectText(
        string? text,
        ConversationMessageRole role,
        AgentMessageType messageType,
        DateTimeOffset createdAt)
    {
        return text is null
            ? null
            : new ConversationHistoryMessage(
                role,
                new AgentMessage(messageType, text, createdAt: createdAt));
    }

    /// <summary>
    /// 投影 toolCall Item：历史渲染只显示工具名称，不带参数与执行结果。
    /// </summary>
    /// <param name="payload">Item 载荷 JSON。</param>
    /// <param name="createdAt">落库时间。</param>
    /// <returns>历史消息；载荷非法时返回 <see langword="null" />。</returns>
    private static ConversationHistoryMessage? ProjectToolCall(string payload, DateTimeOffset createdAt)
    {
        var toolPayload = ConversationItemJsonSerializerContext.DeserializePayload<ToolCallItemPayload>(payload);
        if (toolPayload is null)
        {
            return null;
        }

        // 参数与结果仍留存于 payload，仅历史视图保持简洁
        return new ConversationHistoryMessage(
            ConversationMessageRole.tool,
            new AgentMessage(AgentMessageType.ToolCall, toolPayload.ToolName, createdAt: createdAt));
    }

    /// <summary>
    /// 投影 toolApprovalRequest Item：审批上下文 JSON 随消息回传，历史重载后可继续响应审批。
    /// </summary>
    /// <param name="payload">Item 载荷 JSON。</param>
    /// <param name="createdAt">落库时间。</param>
    /// <returns>历史消息；载荷非法时返回 <see langword="null" />。</returns>
    private static ConversationHistoryMessage? ProjectToolApprovalRequest(string payload, DateTimeOffset createdAt)
    {
        var approvalPayload =
            ConversationItemJsonSerializerContext.DeserializePayload<ToolApprovalRequestItemPayload>(payload);
        if (approvalPayload is null)
        {
            return null;
        }

        return new ConversationHistoryMessage(
            ConversationMessageRole.assistant,
            new AgentMessage(
                AgentMessageType.ToolApprovalRequest,
                $"{approvalPayload.ToolName}()",
                approvalPayload.ApprovalContent,
                createdAt));
    }
}
