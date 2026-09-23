using NarutoCode.Domain.Messages;

namespace NarutoCode.Domain.Conversations;

/// <summary>
/// 会话 Item 实体，对应表 agent_session_items。
/// 承载 UI 渲染历史的持久化形态：默认仅完成态落库；
/// 唯一例外是用户交互（<see cref="ConversationItemKinds.UserInteraction"/>）——
/// pending 态即落库，作为进程重启后恢复交互等待态的状态源，终态经回写更新。
/// </summary>
public class ConversationItem
{
    /// <summary>
    /// 创建会话 Item，雪花 ID 在构造时生成。
    /// </summary>
    public ConversationItem()
    {
        Id = SnowflakeIdHelper.Instance.NextId();
    }

    /// <summary>
    /// Item ID（雪花主键，应用层生成）。
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 所属会话标识（应用层引用 agent_sessions.id；无外键）。
    /// </summary>
    public long SessionId { get; set; }

    /// <summary>
    /// Item 类型判别值，取值见 <see cref="ConversationItemKinds" />。
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Item 状态，取值见 <see cref="ConversationItemStatuses" />。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Item 完整多态 JSON（结构随 Kind 不同：消息类为消息载荷，
    /// 用户交互为请求/结果复合载荷）。
    /// </summary>
    public string Payload { get; set; } = string.Empty;
}

/// <summary>
/// 会话 Item（agent_session_items）kind 列的取值常量（camelCase 风格）。
/// </summary>
public static class ConversationItemKinds
{
    /// <summary>用户真实输入消息。</summary>
    public const string UserMessage = "userMessage";

    /// <summary>助手输出文本消息。</summary>
    public const string AgentMessage = "agentMessage";

    /// <summary>思考（推理过程）内容。</summary>
    public const string Reasoning = "reasoning";

    /// <summary>工具调用。</summary>
    public const string ToolCall = "toolCall";

    /// <summary>工具审批请求（最新一条渲染为审批卡片，历史渲染为工具调用）。</summary>
    public const string ToolApprovalRequest = "toolApprovalRequest";

    /// <summary>错误。</summary>
    public const string Error = "error";

    /// <summary>
    /// 用户交互问答卡片（ask_user 提问/选择/输入）。
    /// pending 态即落库作为进程重启后恢复交互等待态的状态源，终态经回写更新同一行。
    /// </summary>
    public const string UserInteraction = "userInteraction";

    /// <summary>
    /// 将 Item 类型映射为 TUI 渲染使用的消息类型枚举。
    /// </summary>
    /// <param name="kind">Item 类型常量。</param>
    /// <returns>消息类型枚举。</returns>
    public static AgentMessageType ToMessageType(string kind) => kind switch
    {
        Reasoning => AgentMessageType.Thinking,
        ToolCall => AgentMessageType.ToolCall,
        ToolApprovalRequest => AgentMessageType.ToolApprovalRequest,
        Error => AgentMessageType.Error,
        _ => AgentMessageType.Content
    };
}

/// <summary>
/// 会话 Item（agent_session_items）status 列的取值常量。
/// </summary>
public static class ConversationItemStatuses
{
    /// <summary>等待中（仅用户交互 Item 使用，等待态即落库以支持重启恢复）。</summary>
    public const string Pending = "pending";

    /// <summary>成功完成。</summary>
    public const string Succeeded = "succeeded";

    /// <summary>失败（error 类 Item 使用）。</summary>
    public const string Failed = "failed";

    /// <summary>已取消（用户取消 / 进程重启启动清理）。</summary>
    public const string Cancelled = "cancelled";

    /// <summary>超时未应答（预留）。</summary>
    public const string Expired = "expired";
}
