using NarutoCode.Domain.Messages;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 会话入口流程的最终选择结果。
/// </summary>
/// <param name="ShouldExit">是否退出应用。</param>
/// <param name="CreateNew">是否创建新会话。</param>
/// <param name="ConversationId">需要加载的已有会话标识。</param>
internal sealed record SessionLauncherResult(
    bool ShouldExit,
    bool CreateNew,
    ConversationSessionId? ConversationId)
{
    /// <summary>
    /// 创建退出应用的选择结果。
    /// </summary>
    public static SessionLauncherResult Exit() => new(true, false, null);

    /// <summary>
    /// 创建新会话的选择结果。
    /// </summary>
    public static SessionLauncherResult NewConversation() => new(false, true, null);

    /// <summary>
    /// 创建加载既有会话的选择结果。
    /// </summary>
    /// <param name="conversationId">既有会话标识。</param>
    public static SessionLauncherResult Existing(ConversationSessionId conversationId) => new(false, false, conversationId);
}
