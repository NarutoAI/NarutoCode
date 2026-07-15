namespace NarutoCode.Desktop.Api.Errors;

/// <summary>
/// 统一错误响应。
/// </summary>
/// <param name="Code">错误代码。</param>
/// <param name="Message">错误描述。</param>
/// <param name="TraceId">追踪标识。</param>
/// <param name="Details">附加详情。</param>
public sealed record ApiErrorResponse(string Code, string Message, string TraceId, object? Details);
