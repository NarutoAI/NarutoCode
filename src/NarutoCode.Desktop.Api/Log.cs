using Microsoft.Extensions.Logging;

namespace NarutoCode.Desktop.Api;

/// <summary>
/// 桌面端 API 源生成日志声明，禁止直接调用 logger.LogInformation/LogError。
/// </summary>
internal static partial class Log
{
    [LoggerMessage(1000, LogLevel.Information, "Desktop API listening on port {Port}.")]
    public static partial void DesktopApiStarted(ILogger logger, int port);

    [LoggerMessage(1001, LogLevel.Error, "Desktop API startup failed.")]
    public static partial void DesktopApiStartupFailed(ILogger logger, Exception exception);

    [LoggerMessage(1002, LogLevel.Warning, "Parent process monitor encountered an unexpected error.")]
    public static partial void ParentProcessMonitorFailed(ILogger logger, Exception exception);

    [LoggerMessage(1003, LogLevel.Error, "Run {RunId} pump failed.")]
    public static partial void RunPumpFailed(ILogger logger, string runId, Exception exception);

    [LoggerMessage(1004, LogLevel.Error, "Desktop API server error: {Code}.")]
    public static partial void DesktopApiServerError(ILogger logger, string code, Exception exception);

    [LoggerMessage(1005, LogLevel.Information, "Run {RunId} emitted event {Sequence} ({EventType}).")]
    public static partial void RunEventEmitted(ILogger logger, string runId, long sequence, string eventType);
}
