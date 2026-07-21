namespace NarutoCode.Gateway;

/// <summary>
/// 网关启动参数。
/// </summary>
public sealed class GatewayOptions
{
    /// <summary>
    /// 网关配置文件路径，默认为 ~/.narutocode/gateway.json。
    /// </summary>
    public string ConfigPath { get; init; }

    /// <summary>
    /// 应用数据目录，默认为 ~/.narutocode。
    /// </summary>
    public string AppDataDirectory { get; init; }

    private GatewayOptions(string configPath, string appDataDirectory)
    {
        ConfigPath = configPath;
        AppDataDirectory = appDataDirectory;
    }

    /// <summary>
    /// 从命令行参数解析启动选项，支持 --config &lt;路径&gt; 覆盖配置文件位置。
    /// </summary>
    public static GatewayOptions Parse(string[] args)
    {
        var defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".narutocode");

        var configPath = Path.Combine(defaultDir, "gateway.json");

        // 解析 --config 参数
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length)
            {
                configPath = args[i + 1];
            }
        }

        return new GatewayOptions(configPath, defaultDir);
    }
}
