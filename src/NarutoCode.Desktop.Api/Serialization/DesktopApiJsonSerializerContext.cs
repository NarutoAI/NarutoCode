using System.Text.Json.Serialization;
using NarutoCode.Desktop.Api.Contracts;
using NarutoCode.Desktop.Api.Errors;

namespace NarutoCode.Desktop.Api.Serialization;

/// <summary>
/// 桌面端 API 的 Native AOT 兼容 JSON 序列化上下文。
/// 所有需要序列化的 DTO 都必须在此注册。
/// </summary>
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ReadyResponse))]
[JsonSerializable(typeof(LlmSettingsResponse))]
[JsonSerializable(typeof(SwitchProviderRequest))]
[JsonSerializable(typeof(SwitchEffortRequest))]
[JsonSerializable(typeof(WorkspaceSummaryDto))]
[JsonSerializable(typeof(OpenWorkspaceRequest))]
[JsonSerializable(typeof(OpenWorkspaceResponse))]
[JsonSerializable(typeof(ConversationSummaryDto))]
[JsonSerializable(typeof(ConversationHistoryMessageDto))]
[JsonSerializable(typeof(ConversationHistoryDto))]
[JsonSerializable(typeof(StartRunRequest))]
[JsonSerializable(typeof(StartRunResponse))]
[JsonSerializable(typeof(ResolveApprovalRequest))]
[JsonSerializable(typeof(RunEventDto))]
[JsonSerializable(typeof(ApiErrorResponse))]
[JsonSerializable(typeof(List<WorkspaceSummaryDto>))]
[JsonSerializable(typeof(List<ConversationSummaryDto>))]
internal partial class DesktopApiJsonSerializerContext : JsonSerializerContext;
