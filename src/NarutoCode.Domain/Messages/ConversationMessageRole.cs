namespace NarutoCode.Domain.Messages;

/// <summary>
/// 会话消息生产者角色，不依赖 UI 文案或具体模型供应商 SDK。
/// </summary>
public enum ConversationMessageRole
{
    /// <summary>
    /// 系统消息，用于描述会话级规则或约束。
    /// </summary>
    system = 0,

    /// <summary>
    /// 用户输入消息。
    /// </summary>
    user = 1,

    /// <summary>
    /// 助手输出消息。
    /// </summary>
    assistant = 2,

    /// <summary>
    /// 工具调用或工具结果消息。
    /// </summary>
    tool = 3
}
