namespace NarutoCode.Domain.Conversations;

/// <summary>
/// 项目及其工作目录关联会话的摘要。
/// </summary>
/// <param name="Id">项目标识。</param>
/// <param name="Name">项目显示名称。</param>
/// <param name="WorkDirectory">项目工作目录的规范化绝对路径。</param>
/// <param name="SortOrder">项目排序值，值越小越靠前。</param>
/// <param name="CreatedAt">项目创建时间。</param>
/// <param name="UpdatedAt">项目最近更新时间。</param>
/// <param name="LastUpdatedAt">关联会话的最近更新时间；没有会话时等于项目更新时间。</param>
/// <param name="ConversationCount">关联会话数量。</param>
public sealed record WorkspaceSummary(
    long Id,
    string Name,
    string WorkDirectory,
    int SortOrder,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime LastUpdatedAt,
    int ConversationCount);
