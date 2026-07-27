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
/// <param name="SourceId">来源标识，用于匹配对应通道会话（如企业微信的 aibotid_senderId 或 chatId）。</param>
/// <param name="Attachments">消息附件（如图片本地路径），无附件时为空数组。</param>
public sealed record GatewayInboundMessage(
    string ChannelId,
    string SenderId,
    string? SenderName,
    string Text,
    string? MessageId,
    string? ReplyToId,
    string? GroupId,
    bool IsGroup,
    string? SourceId,
    IReadOnlyList<GatewayInboundAttachment> Attachments);

/// <summary>
/// 通道入站消息附件（如图片），以内存字节承载。
/// </summary>
/// <param name="Data">附件二进制数据。</param>
/// <param name="MediaType">附件媒体类型（如 image/jpeg）。</param>
public sealed record GatewayInboundAttachment(byte[] Data, string MediaType);
