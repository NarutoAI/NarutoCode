namespace NarutoCode.Desktop.Api.Contracts;

/// <summary>
/// 工作区摘要 DTO。
/// </summary>
/// <param name="Id">工作区哈希标识。</param>
/// <param name="Name">工作区名称。</param>
/// <param name="WorkDirectory">工作区绝对路径。</param>
/// <param name="LastUpdatedAt">最近更新时间。</param>
/// <param name="ConversationCount">会话数量。</param>
/// <param name="DirectoryExists">工作目录是否仍然存在。</param>
public sealed record WorkspaceSummaryDto(
    string Id,
    string Name,
    string WorkDirectory,
    DateTime LastUpdatedAt,
    int ConversationCount,
    bool DirectoryExists);

/// <summary>
/// 打开工作区请求。
/// </summary>
/// <param name="WorkDirectory">工作目录路径。</param>
public sealed record OpenWorkspaceRequest(string WorkDirectory);

/// <summary>
/// 打开工作区响应。
/// </summary>
/// <param name="WorkspaceId">工作区哈希标识。</param>
/// <param name="Conversation">打开后的会话信息。</param>
/// <param name="Created">是否新建了首个会话。</param>
public sealed record OpenWorkspaceResponse(
    string WorkspaceId,
    ConversationSummaryDto Conversation,
    bool Created);
