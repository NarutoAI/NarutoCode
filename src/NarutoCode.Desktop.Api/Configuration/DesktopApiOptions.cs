using System.Collections;
using NarutoCode.Domain;

namespace NarutoCode.Desktop.Api.Configuration;

/// <summary>
/// 桌面端 API 启动选项，从环境变量解析。
/// </summary>
internal sealed class DesktopApiOptions
{
    /// <summary>
    /// Bearer 认证令牌。
    /// </summary>
    public string Token { get; private init; } = string.Empty;

    /// <summary>
    /// 监听端口，0 表示由操作系统分配。
    /// </summary>
    public int Port { get; private init; }

    /// <summary>
    /// 父进程标识，用于父进程退出时自动关闭 API。
    /// </summary>
    public int? ParentProcessId { get; private init; }

    /// <summary>
    /// 应用数据目录，默认为 <see cref="ProjectConstant.AppDirectory"/>。
    /// </summary>
    public string AppDataDirectory { get; private init; } = ProjectConstant.AppDirectory;

    /// <summary>
    /// 从环境变量集合解析启动选项，校验必填项和格式。
    /// </summary>
    /// <param name="environmentVariables">环境变量字典。</param>
    /// <returns>解析后的启动选项。</returns>
    /// <exception cref="InvalidOperationException">缺少令牌、端口非法或数据目录非绝对路径时抛出。</exception>
    public static DesktopApiOptions Parse(IDictionary environmentVariables)
    {
        // 读取 Bearer 令牌，必须存在
        var token = environmentVariables["NARUTOCODE_DESKTOP_TOKEN"] as string;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("缺少环境变量 NARUTOCODE_DESKTOP_TOKEN。");
        }

        // 读取端口，默认 0 由操作系统分配
        var portRaw = environmentVariables["NARUTOCODE_DESKTOP_PORT"] as string;
        var port = 0;
        if (!string.IsNullOrWhiteSpace(portRaw) &&
            (!int.TryParse(portRaw, out port) || port < 0))
        {
            throw new InvalidOperationException($"环境变量 NARUTOCODE_DESKTOP_PORT 无效：{portRaw}");
        }

        // 读取父进程标识，可选
        int? parentProcessId = null;
        var parentPidRaw = environmentVariables["NARUTOCODE_DESKTOP_PARENT_PID"] as string;
        if (!string.IsNullOrWhiteSpace(parentPidRaw))
        {
            if (!int.TryParse(parentPidRaw, out var parentPid))
            {
                throw new InvalidOperationException(
                    $"环境变量 NARUTOCODE_DESKTOP_PARENT_PID 无效：{parentPidRaw}");
            }

            parentProcessId = parentPid;
        }

        // 读取应用数据目录，默认使用 ProjectConstant.AppDirectory
        var appDataDirectory = environmentVariables["NARUTOCODE_APP_DATA_DIRECTORY"] as string;
        if (string.IsNullOrWhiteSpace(appDataDirectory))
        {
            appDataDirectory = ProjectConstant.AppDirectory;
        }

        if (!Path.IsPathRooted(appDataDirectory))
        {
            throw new InvalidOperationException(
                $"环境变量 NARUTOCODE_APP_DATA_DIRECTORY 必须为绝对路径：{appDataDirectory}");
        }

        return new DesktopApiOptions
        {
            Token = token,
            Port = port,
            ParentProcessId = parentProcessId,
            AppDataDirectory = appDataDirectory
        };
    }
}
