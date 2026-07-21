using System.Text.Json;
using System.Text.Json.Serialization;

namespace NarutoCode.Gateway.Configuration;

/// <summary>
/// 网关配置，聚合固定工作目录和各通道配置。
/// </summary>
public sealed class GatewayConfiguration
{
    /// <summary>
    /// 固定工作目录路径，所有通道消息交给该目录的 Agent 会话处理。
    /// </summary>
    public string Workspace { get; set; } = string.Empty;

    /// <summary>
    /// 企业微信通道配置，Enabled=false 时不启动。
    /// </summary>
    public WeComConfiguration WeCom { get; set; } = new();

    /// <summary>
    /// 从指定路径加载网关配置，环境变量凭据覆盖配置文件值。
    /// </summary>
    /// <param name="configPath">gateway.json 路径。</param>
    /// <returns>解析后的网关配置。</returns>
    public static async Task<GatewayConfiguration> LoadAsync(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                $"网关配置文件不存在：{configPath}。请创建 gateway.json 并填写工作目录和通道凭据。",
                configPath);
        }

        await using var stream = new FileStream(
            configPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var config = await JsonSerializer.DeserializeAsync(
            stream,
            GatewayConfigurationContext.Default.GatewayConfiguration);

        if (config is null)
        {
            throw new InvalidOperationException("网关配置文件无效，请检查 JSON 格式。");
        }

        if (string.IsNullOrWhiteSpace(config.Workspace))
        {
            throw new InvalidOperationException("网关配置缺少 workspace 字段。");
        }

        // 环境变量覆盖凭据（避免明文落盘）
        ApplyEnvironmentOverrides(config.WeCom);

        return config;
    }

    /// <summary>
    /// 用环境变量覆盖企业微信凭据，环境变量未设置时保留配置文件值。
    /// </summary>
    private static void ApplyEnvironmentOverrides(WeComConfiguration weCom)
    {
        if (Environment.GetEnvironmentVariable("WECOM_BOT_ID") is { } botId)
            weCom.BotId = botId;
        if (Environment.GetEnvironmentVariable("WECOM_BOT_SECRET") is { } botSecret)
            weCom.BotSecret = botSecret;
        if (Environment.GetEnvironmentVariable("WECOM_CORP_ID") is { } corpId)
            weCom.CorpId = corpId;
        if (Environment.GetEnvironmentVariable("WECOM_CORP_SECRET") is { } corpSecret)
            weCom.CorpSecret = corpSecret;
        if (int.TryParse(Environment.GetEnvironmentVariable("WECOM_AGENT_ID"), out var agentId))
            weCom.AgentId = agentId;
    }
}

/// <summary>
/// 网关配置 JSON 序列化上下文，AOT 兼容。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(GatewayConfiguration))]
internal partial class GatewayConfigurationContext : JsonSerializerContext;
