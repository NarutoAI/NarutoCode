namespace NarutoCode.Domain.Messages;

/// <summary>
/// Agent 用户消息附件，用于表达图片等多模态输入。
/// </summary>
/// <param name="FilePath">附件本地文件路径。</param>
/// <param name="MediaType">附件媒体类型。</param>
public sealed record AgentMessageAttachment(string FilePath, string MediaType);
