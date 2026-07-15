namespace NarutoCode.Desktop.Api.Runs;

/// <summary>
/// 指定的会话不存在时抛出。
/// </summary>
internal sealed class ConversationNotFoundException(long conversationId)
    : InvalidOperationException($"会话 {conversationId} 不存在。")
{
    /// <summary>未找到的会话标识。</summary>
    public long ConversationId { get; } = conversationId;
}
