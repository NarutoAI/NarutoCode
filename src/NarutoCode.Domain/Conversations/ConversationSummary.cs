namespace NarutoCode.Domain.Conversations;

/// <summary>
/// 当前工作目录下用于入口页展示的会话摘要。
/// </summary>
/// <param name="Id">会话标识。</param>
/// <param name="Title">会话标题。</param>
/// <param name="CreatedAt">创建时间。</param>
/// <param name="UpdatedAt">最后更新时间。</param>
/// <param name="MessageCount">可见消息数量。</param>
/// <param name="LastUserMessagePreview">最后一条用户消息摘要。</param>
public sealed record ConversationSummary(
    long Id,
    string Title,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int MessageCount,
    string LastUserMessagePreview);
