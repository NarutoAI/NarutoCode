namespace NarutoCode.Gateway.Configuration;

/// <summary>
/// 企业微信 AI 机器人通道配置。
/// 使用 WebSocket 长连接接收消息，REST API 降级发送。
/// </summary>
public sealed class WeComConfiguration
{
    /// <summary>
    /// 是否启用企业微信通道。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// AI 机器人 BotId，格式 aib-xxxxx。优先读环境变量 WECOM_BOT_ID。
    /// </summary>
    public string? BotId { get; set; }

    /// <summary>
    /// AI 机器人长连接 Secret。优先读环境变量 WECOM_BOT_SECRET。
    /// </summary>
    public string? BotSecret { get; set; }

    /// <summary>
    /// 企业 CorpID，REST API 降级发送使用。优先读环境变量 WECOM_CORP_ID。
    /// </summary>
    public string? CorpId { get; set; }

    /// <summary>
    /// 自建应用 Secret，用于获取 access_token。优先读环境变量 WECOM_CORP_SECRET。
    /// </summary>
    public string? CorpSecret { get; set; }

    /// <summary>
    /// 自建应用 AgentId。优先读环境变量 WECOM_AGENT_ID。
    /// </summary>
    public int AgentId { get; set; }

    /// <summary>
    /// 入站消息最大字符数，超出截断。
    /// </summary>
    public int MaxInboundChars { get; set; } = 4096;
}
