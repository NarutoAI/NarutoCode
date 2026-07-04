using System.Text.Json;
using System.Text.Json.Serialization;

namespace NarutoCode.Domain.Configurations;

[JsonSourceGenerationOptions(
    WriteIndented = true, 
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, 
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(AppConfiguration))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(McpServerConfiguration))]
internal partial class AppConfigurationContext : JsonSerializerContext
{
}

/// <summary>
/// 程序配置，聚合运行时需要读取的核心配置。
/// </summary>
public sealed class AppConfiguration
{
    /// <summary>
    /// LLM 模型配置集合，provider 必须唯一。
    /// </summary>
    public List<LlmConfiguration> Llms { get; set; } = [];

    /// <summary>
    /// 系统运行配置。
    /// </summary>
    public SystemConfiguration System { get; set; } = new();

    /// <summary>
    /// MCP 服务配置集合，键为服务名称。
    /// </summary>
    public Dictionary<string, McpServerConfiguration> McpServers { get; set; } = [];

    /// <summary>
    /// 是否开启工具调用审批，默认关闭。
    /// </summary>
    public bool EnableApproval { get; set; }

    /// <summary>
    /// 最大交互次数
    /// </summary>
    public int MaxTurnCount { get; set; } = 10;
}

/// <summary>
/// MCP 服务配置，第一版仅支持 stdio 类型。
/// </summary>
public sealed class McpServerConfiguration
{
    /// <summary>
    /// MCP 服务传输类型，第一版仅支持 stdio。
    /// </summary>
    public string Type { get; set; } = "stdio";

    /// <summary>
    /// MCP 服务启动命令。
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// MCP 服务启动参数。
    /// </summary>
    public List<string> Args { get; set; } = [];

    /// <summary>
    /// MCP 服务用途描述，会拼接到该服务暴露工具的描述前面，帮助 AI 区分多个同类 MCP 服务。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// MCP 服务启动工作目录；为空时使用当前进程工作目录。
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// MCP 服务启动时附加的环境变量。
    /// </summary>
    public Dictionary<string, string> Env { get; set; } = [];

    /// <summary>
    /// 是否启用该 MCP 服务。
    /// </summary>
    public bool Enabled { get; set; } = true;
}
