namespace NarutoCode.Domain.Messages;

/// <summary>
/// 消息可见性，用于区分真实聊天内容与框架内部上下文消息。
/// </summary>
public enum MessageVisibility
{
    /// <summary>
    /// 可在聊天界面展示，并可作为历史消息恢复到 Agent 上下文。
    /// </summary>
    Visible = 0,

    /// <summary>
    /// 仅作为框架内部上下文保留，不在聊天界面展示，也不恢复到 Agent 历史上下文。
    /// </summary>
    Hidden = 1
}
