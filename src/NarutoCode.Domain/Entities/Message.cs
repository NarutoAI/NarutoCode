using NarutoCode.Domain.Messages;

namespace NarutoCode.Domain.Entities;

/// <summary>
/// 消息实体
/// 表示对话中的一条消息
/// </summary>
public class Message
{
    public Message()
    {
        Id = SnowflakeIdHelper.Instance.NextId();
    }

    /// <summary>
    /// 消息ID（主键）
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 所属对话ID（外键）
    /// </summary>
    public long ConversationId { get; set; }

    /// <summary>
    /// 所属对话
    /// </summary>
    public virtual Conversation? Conversation { get; private set; }

    /// <summary>
    /// 消息角色
    /// user: 用户消息
    /// assistant: AI助手消息
    /// system: 系统消息
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 模型内容
    /// </summary>
    public string ModelContent { get; set; } = string.Empty;

    /// <summary>
    /// 工具调用标识，用于恢复工具审批请求和审批响应的关联。
    /// </summary>
    public string ToolApprovalContent { get; set; } = string.Empty;

    /// <summary>
    /// 消息创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 内容类型（可选，用于区分不同类型的消息，如文本、图片、文件等）
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// 消息类型
    /// </summary>
    public AgentMessageType MessageType { get; set; }

    /// <summary>
    /// 消息可见性，用于过滤框架内部补充的上下文消息。
    /// </summary>
    public MessageVisibility Visibility { get; set; } = MessageVisibility.Visible;

    /// <summary>
    /// 消息的Token数量（可选，用于统计）
    /// </summary>
    public int? TokenCount { get; set; }
}