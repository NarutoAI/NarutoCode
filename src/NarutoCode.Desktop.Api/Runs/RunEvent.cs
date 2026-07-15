using NarutoCode.Domain.Messages;

namespace NarutoCode.Desktop.Api.Runs;

/// <summary>
/// Run 事件，包含序号、类型和可选的 Agent 消息。
/// </summary>
/// <param name="RunId">Run 标识。</param>
/// <param name="Sequence">事件序号，单调递增。</param>
/// <param name="EventType">事件类型字符串。</param>
/// <param name="Timestamp">事件时间戳。</param>
/// <param name="Message">关联的 Agent 消息，可为空。</param>
/// <param name="ApprovalId">审批标识，可为空。</param>
internal sealed record RunEvent(
    string RunId,
    long Sequence,
    string EventType,
    DateTimeOffset Timestamp,
    AgentMessage? Message = null,
    string? ApprovalId = null);
