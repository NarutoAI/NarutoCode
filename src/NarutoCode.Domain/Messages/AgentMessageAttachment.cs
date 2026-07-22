namespace NarutoCode.Domain.Messages;

/// <summary>
/// Agent 用户消息附件，用于表达图片等多模态输入。
/// 附件以内存字节方式承载，无需落盘。
/// </summary>
/// <param name="Data">附件二进制数据。</param>
/// <param name="MediaType">附件媒体类型（如 image/jpeg）。</param>
public sealed record AgentMessageAttachment(byte[] Data, string MediaType);
