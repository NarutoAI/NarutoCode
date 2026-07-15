using System.Text.Json.Serialization;

namespace NarutoCode.Desktop.Api.Contracts;

/// <summary>
/// 启动 Run 请求。
/// </summary>
/// <param name="Content">用户消息内容。</param>
/// <param name="Attachments">附件列表。</param>
public sealed record StartRunRequest(
    string Content,
    IReadOnlyList<RunAttachmentRequest>? Attachments);

/// <summary>
/// Run 附件请求。
/// </summary>
public sealed record RunAttachmentRequest(string Path, string MediaType);

/// <summary>
/// 启动 Run 响应。
/// </summary>
public sealed record StartRunResponse(string RunId, string Status, string EventsUrl);

/// <summary>
/// 解析审批请求。
/// </summary>
public sealed record ResolveApprovalRequest(bool Approved);

/// <summary>
/// SSE 事件 DTO。
/// </summary>
public sealed record RunEventDto(
    [property: JsonPropertyName("runId")] string RunId,
    [property: JsonPropertyName("sequence")] long Sequence,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("messageType")] string? MessageType,
    [property: JsonPropertyName("approvalContent")] string? ApprovalContent,
    [property: JsonPropertyName("approvalId")] string? ApprovalId);
