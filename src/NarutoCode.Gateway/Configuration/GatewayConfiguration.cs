using System.Text.Json;
using System.Text.Json.Serialization;

namespace NarutoCode.Gateway.Configuration;

/// <summary>
/// Gateway 通道绑定配置。
/// </summary>
public sealed class GatewayConfiguration
{
    /// <summary>
    /// 企业微信机器人绑定集合。
    /// </summary>
    public List<GatewayBotBinding> WeComBots { get; set; } = [];

    /// <summary>
    /// 加载并校验 Gateway 配置。
    /// </summary>
    public static async Task<GatewayConfiguration> LoadAsync(string configPath)
    {
        await using var stream = File.OpenRead(configPath);
        var config =
            await JsonSerializer.DeserializeAsync(stream, GatewayConfigurationContext.Default.GatewayConfiguration)
            ?? throw new InvalidOperationException("网关配置文件无效，请检查 JSON 格式。");
        if (config.WeComBots.Count == 0) throw new InvalidOperationException("网关配置至少需要一个 wecomBots 绑定。");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var workspaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in config.WeComBots)
        {
            if (string.IsNullOrWhiteSpace(binding.Id) || string.IsNullOrWhiteSpace(binding.Workspace))
                throw new InvalidOperationException("wecomBots 绑定缺少 id 或 workspace。");
            binding.Workspace = Path.GetFullPath(binding.Workspace);
            if (!ids.Add(binding.Id) || !workspaces.Add(binding.Workspace))
                throw new InvalidOperationException("wecomBots 的 id 或 workspace 重复。");
        }

        return config;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
    WriteIndented = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(GatewayConfiguration))]
internal partial class GatewayConfigurationContext : JsonSerializerContext;