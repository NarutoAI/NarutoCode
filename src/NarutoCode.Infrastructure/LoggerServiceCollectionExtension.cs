using Microsoft.Extensions.DependencyInjection;
using NarutoCode.Domain;
using Serilog;
using Serilog.Events;

namespace NarutoCode.Infrastructure;

/// <summary>
/// 日志
/// </summary>
internal static class LoggerServiceCollectionExtension
{
    /// <param name="service"></param>
    extension(IServiceCollection service)
    {
        /// <summary>
        /// 注册日志，可指定滚动日志文件名前缀。
        /// </summary>
        /// <param name="logFileName">日志文件名前缀，默认 <c>.log</c>。</param>
        public void AddLogger(string logFileName = ".log")
        {
            var loggerConfiguration = new LoggerConfiguration();
            loggerConfiguration.MinimumLevel.Is(ResolveMinimumLogLevel());
            loggerConfiguration.AddFile(logFileName);

            Log.Logger = loggerConfiguration
                .CreateLogger();
            service.AddSerilog();
        }
    }


    private static LogEventLevel ResolveMinimumLogLevel()
    {
        var configuredLogLevel = AppData.Config.System.LogLevel;
        if (string.IsNullOrWhiteSpace(configuredLogLevel))
        {
            return LogEventLevel.Error;
        }

        return configuredLogLevel.Trim().ToLowerInvariant() switch
        {
            "trace" => LogEventLevel.Verbose,
            "critical" => LogEventLevel.Fatal,
            _ when Enum.TryParse<LogEventLevel>(configuredLogLevel, ignoreCase: true, out var logLevel) => logLevel,
            _ => LogEventLevel.Error
        };
    }

    private static void AddFile(this LoggerConfiguration loggerConfiguration, string logFileName)
    {
        loggerConfiguration.WriteTo.Async(a => a.File(
            Path.Combine(Path.Combine(ProjectConstant.AppDirectory, FileLogDirectory), logFileName),
            rollingInterval: RollingInterval.Day,
            rollOnFileSizeLimit: true
        ));
    }

    private const string FileLogDirectory = "Logs";
}