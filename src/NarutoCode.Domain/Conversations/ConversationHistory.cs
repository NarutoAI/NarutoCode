using NarutoCode.Domain.Messages;

namespace NarutoCode.Domain.Conversations;

/// <summary>
/// 当前工作目录对应的对话历史，供应用层加载后交给表现层恢复界面状态。
/// </summary>
/// <param name="SessionId">对话会话标识。</param>
/// <param name="Messages">按创建顺序排列的历史消息。</param>
/// <param name="TokenCount">会话进入前已经累计的 Token 使用量。</param>
public sealed record ConversationHistory(
    ConversationSessionId SessionId,
    IReadOnlyList<ConversationHistoryMessage> Messages,
    long TokenCount = 0);
