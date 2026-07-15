using System.Diagnostics;
using NarutoCode.Desktop.Api.Configuration;

namespace NarutoCode.Desktop.Api.Hosting;

/// <summary>
/// 后台服务，监控父进程存活状态，父进程退出时停止 API。
/// </summary>
internal sealed class ParentProcessMonitor(
    DesktopApiOptions options,
    IHostApplicationLifetime lifetime,
    ILogger<ParentProcessMonitor> logger) : BackgroundService
{
    /// <summary>
    /// 定期检查父进程是否存活，不存在或已退出时停止应用。
    /// </summary>
    /// <param name="stoppingToken">停止令牌。</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 未配置父进程时直接返回
        if (options.ParentProcessId is null)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 检查父进程是否仍然存活
                using var process = Process.GetProcessById(options.ParentProcessId.Value);
                if (process.HasExited)
                {
                    lifetime.StopApplication();
                    return;
                }
            }
            catch (ArgumentException)
            {
                // 进程不存在，停止应用
                lifetime.StopApplication();
                return;
            }
            catch (Exception exception)
            {
                // 记录意外异常但不中断监控
                Log.ParentProcessMonitorFailed(logger, exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
