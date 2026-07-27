namespace NarutoCode.Gateway.Configuration;

/// <summary>
/// 企业微信机器人与根工作目录的绑定。
/// </summary>
public sealed class GatewayBotBinding
{
    /// <summary>
    /// Gateway 内部绑定标识。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 消息进入的根工作目录。
    /// </summary>
    public string Workspace { get; set; } = string.Empty;

    /// <summary>
    /// 是否启动绑定通道。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 企业微信机器人标识。
    /// </summary>
    public string? BotId { get; set; }

    /// <summary>
    /// 机器人密钥。
    /// </summary>
    public string? BotSecret { get; set; }

    /// <summary>
    /// 企业标识。
    /// </summary>
    public string? CorpId { get; set; }

    /// <summary>
    /// 企业应用密钥。
    /// </summary>
    public string? CorpSecret { get; set; }

    /// <summary>
    /// REST 降级回复使用的应用标识。
    /// </summary>
    public int AgentIdForRestApi { get; set; }

    /// <summary>
    /// 最大入站字符数。
    /// </summary>
    public int MaxInboundChars { get; set; } = 4096;
}