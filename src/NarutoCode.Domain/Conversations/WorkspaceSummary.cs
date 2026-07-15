namespace NarutoCode.Domain.Conversations;

/// <summary>
/// 按工作目录聚合的工作区摘要。
/// </summary>
/// <param name="WorkDirectory">工作区绝对路径。</param>
/// <param name="LastUpdatedAt">工作区最近一次会话更新时间。</param>
/// <param name="ConversationCount">工作区会话数量。</param>
public sealed record WorkspaceSummary(
    string WorkDirectory,
    DateTime LastUpdatedAt,
    int ConversationCount);
