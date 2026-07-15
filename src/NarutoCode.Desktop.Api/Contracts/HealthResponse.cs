namespace NarutoCode.Desktop.Api.Contracts;

/// <summary>
/// 健康检查响应。
/// </summary>
/// <param name="Status">服务状态。</param>
/// <param name="Version">API 版本号。</param>
/// <param name="ProcessId">当前进程标识。</param>
public sealed record HealthResponse(string Status, string Version, int ProcessId);
