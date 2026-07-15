namespace NarutoCode.Desktop.Api.Contracts;

/// <summary>
/// 会话摘要 DTO，long ID 序列化为字符串。
/// </summary>
public sealed record ConversationSummaryDto(
    string Id,
    string Title,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int MessageCount,
    long TokenCount,
    long LastUsageTokenCount,
    string LastUserMessagePreview);

/// <summary>
/// 会话历史消息 DTO。
/// </summary>
public sealed record ConversationHistoryMessageDto(
    string Role,
    string MessageType,
    string Content,
    string ApprovalContent,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AttachmentDto> Attachments);

/// <summary>
/// 附件 DTO。
/// </summary>
public sealed record AttachmentDto(string Path, string MediaType);

/// <summary>
/// 会话历史响应。
/// </summary>
public sealed record ConversationHistoryDto(
    string Id,
    long TokenCount,
    IReadOnlyList<ConversationHistoryMessageDto> Messages);

/// <summary>
/// 创建会话请求。
/// </summary>
public sealed record CreateConversationRequest(string? Title);
