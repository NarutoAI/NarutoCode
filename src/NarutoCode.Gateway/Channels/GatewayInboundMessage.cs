namespace NarutoCode.Gateway.Channels;

/// <summary>
/// 通道入站消息统一 DTO，屏蔽不同通道的协议差异。
/// </summary>
/// <param name="ChannelId">通道标识。</param>
/// <param name="SenderId">发送者标识。</param>
/// <param name="SenderName">发送者名称，可能为空。</param>
/// <param name="Text">消息文本内容。</param>
/// <param name="MessageId">消息ID，用于去重。</param>
/// <param name="ReplyToId">回复时需回传的标识（如企业微信的 req_id）。</param>
/// <param name="GroupId">群聊标识，单聊为空。</param>
/// <param name="IsGroup">是否群聊消息。</param>
public sealed record GatewayInboundMessage(
    string ChannelId,
    string SenderId,
    string? SenderName,
    string Text,
    string? MessageId,
    string? ReplyToId,
    string? GroupId,
    bool IsGroup);
