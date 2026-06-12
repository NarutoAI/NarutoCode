using NarutoCode.Domain.Messages;

namespace NarutoCode.Domain.Conversations;

/// <summary>
/// 对话历史中的一条消息，保留消息角色和 Agent 消息类型。
/// </summary>
/// <param name="Role">消息角色。</param>
/// <param name="Message">消息内容。</param>
public sealed record ConversationHistoryMessage(
    ConversationMessageRole Role,
    AgentMessage Message);
