namespace NarutoCode.Desktop.Api.Contracts;

/// <summary>
/// LLM 设置响应。
/// </summary>
/// <param name="CurrentProvider">当前 provider。</param>
/// <param name="CurrentEffort">当前推理强度。</param>
/// <param name="Providers">可用 provider 列表。</param>
/// <param name="Efforts">可用推理强度列表。</param>
public sealed record LlmSettingsResponse(
    string CurrentProvider,
    string CurrentEffort,
    IReadOnlyList<string> Providers,
    IReadOnlyList<string> Efforts);

/// <summary>
/// 切换 provider 请求。
/// </summary>
/// <param name="Provider">目标 provider。</param>
public sealed record SwitchProviderRequest(string Provider);

/// <summary>
/// 切换推理强度请求。
/// </summary>
/// <param name="Effort">目标推理强度。</param>
public sealed record SwitchEffortRequest(string Effort);
