namespace NarutoCode.Desktop.Api.Runs;

/// <summary>
/// 同一会话已有活跃 Run 时抛出。
/// </summary>
internal sealed class RunAlreadyActiveException(long conversationId)
    : InvalidOperationException($"会话 {conversationId} 已有活跃 Run。")
{
    /// <summary>冲突的会话标识。</summary>
    public long ConversationId { get; } = conversationId;
}
