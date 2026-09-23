using NarutoCode.Domain.Messages;

namespace NarutoCode.Domain.Entities;

/// <summary>
/// 消息实体，对应表 agent_chat_messages（append-only 四列设计）。
/// 仅承载 LLM 聊天历史的持久化形态：UI 渲染历史由会话 Item（agent_session_items）承载。
/// </summary>
public class Message
{
    /// <summary>
    /// 创建消息，雪花 ID 在构造时生成。
    /// </summary>
    public Message()
    {
        Id = SnowflakeIdHelper.Instance.NextId();
    }

    /// <summary>
    /// 消息 ID（雪花主键，应用层生成）。
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 所属会话 ID（应用层引用 agent_sessions.id；无外键）。
    /// </summary>
    public long ConversationId { get; set; }

    /// <summary>
    /// 消息角色：user / assistant / tool / system 等 ChatRole 值。
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// 消息类型（snake_case 文本，见 <see cref="AgentMessageTypeNames" />）。
    /// temporary 表示框架临时注入，不进 UI 历史也不恢复到 Agent 上下文。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 模型内容：ChatMessage.Contents 的 AIContent 多态 JSON。
    /// </summary>
    public string ModelContent { get; set; } = string.Empty;

    /// <summary>
    /// 消息落库时间。
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
