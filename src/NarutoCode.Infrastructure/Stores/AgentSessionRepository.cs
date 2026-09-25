using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Entities;
using NarutoCode.Domain.Enums;
using NarutoCode.Domain.Workspaces;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// <see cref="IAgentSessionRepository" /> 的门面实现：仅做跨表读写编排，不直接执行 SQL。
/// 具体持久化职责按表与读写方向分散在：
/// <see cref="AgentWorkspaceReader" /> / <see cref="AgentWorkspaceWriter" />（agent_workspaces）、
/// <see cref="AgentSessionReader" /> / <see cref="AgentSessionWriter" />（agent_sessions）、
/// <see cref="AgentSessionItemReader" />（agent_session_items 面向 UI）、
/// <see cref="AgentChatMessageReader" />（面向模型的聊天历史）。
/// </summary>
/// <param name="workspaceReader">工作区读取。</param>
/// <param name="workspaceWriter">工作区写入。</param>
/// <param name="sessionReader">会话读取。</param>
/// <param name="sessionWriter">会话写入。</param>
/// <param name="sessionItemReader">UI 渲染历史读取。</param>
/// <param name="chatMessageReader">LLM 聊天历史读取。</param>
public sealed class AgentSessionRepository(
    AgentWorkspaceReader workspaceReader,
    AgentWorkspaceWriter workspaceWriter,
    AgentSessionReader sessionReader,
    AgentSessionWriter sessionWriter,
    AgentSessionItemReader sessionItemReader,
    AgentChatMessageReader chatMessageReader) : IAgentSessionRepository
{
    /// <inheritdoc />
    public async Task<Conversation> GetOrCreateByWorkDirectoryAsync(
        string workDirectory,
        CancellationToken cancellationToken = default)
    {
        var normalizedDirectory = NormalizeWorkDirectory(workDirectory);
        var workspaceId = await workspaceWriter.EnsureAsync(normalizedDirectory, cancellationToken);
        var existing = await sessionReader.FindLatestAsync(workspaceId, cancellationToken);
        return existing ?? await sessionWriter.CreateAsync(
            workspaceId,
            ConversationSource.Local,
            string.Empty,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationSummary>> ListByWorkDirectoryAsync(
        string workDirectory,
        CancellationToken cancellationToken = default)
    {
        return await sessionReader.ListByWorkDirectoryAsync(
            NormalizeWorkDirectory(workDirectory),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WorkspaceSummary> GetOrCreateWorkspaceAsync(
        string workDirectory,
        CancellationToken cancellationToken = default)
    {
        var normalizedDirectory = NormalizeWorkDirectory(workDirectory);
        var workspaceId = await workspaceWriter.EnsureAsync(normalizedDirectory, cancellationToken);
        return await workspaceReader.GetSummaryAsync(workspaceId, cancellationToken)
               ?? throw new InvalidOperationException($"工作区创建后无法读取：{normalizedDirectory}");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationSummary>> ListByProjectIdAsync(
        long projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId), "项目标识必须大于零。");
        }

        return await sessionReader.ListByWorkspaceIdAsync(projectId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkspaceSummary>> ListWorkspacesAsync(
        CancellationToken cancellationToken = default)
    {
        return workspaceReader.ListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Conversation> CreateForWorkDirectoryAsync(
        string workDirectory,
        CancellationToken cancellationToken = default)
    {
        var workspaceId = await workspaceWriter.EnsureAsync(
            NormalizeWorkDirectory(workDirectory),
            cancellationToken);
        return await sessionWriter.CreateAsync(
            workspaceId,
            ConversationSource.Local,
            string.Empty,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Conversation> GetOrCreateBySourceAsync(
        long projectId,
        ConversationSource source,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId), "项目标识必须大于零。");
        }

        // 先按来源定位既有会话，不存在则新建（网关按通道复用同一会话）
        var existing = await sessionReader.FindBySourceAsync(projectId, source, sourceId, cancellationToken);
        return existing ?? await sessionWriter.CreateAsync(projectId, source, sourceId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Conversation> CreateForProjectIdAsync(
        long projectId,
        CancellationToken cancellationToken = default)
    {
        return sessionWriter.CreateAsync(projectId, ConversationSource.Local, string.Empty, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Conversation> CreateForProjectIdAsync(
        long projectId,
        ConversationSource source,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        return sessionWriter.CreateAsync(projectId, source, sourceId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Conversation?> GetByIdAsync(long conversationId, CancellationToken cancellationToken = default)
    {
        return sessionReader.GetByIdAsync(conversationId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ConversationHistoryMessage>> ListItemsAsync(
        long conversationId,
        CancellationToken cancellationToken = default)
    {
        return sessionItemReader.ListHistoryAsync(conversationId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Message>> ListMessagesAsync(
        long conversationId,
        CancellationToken cancellationToken = default)
    {
        return chatMessageReader.ListMessagesAsync(conversationId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Message>> ListRuntimeMessagesAsync(
        long conversationId,
        CancellationToken cancellationToken = default)
    {
        return chatMessageReader.ListRuntimeMessagesAsync(conversationId, cancellationToken);
    }

    /// <summary>
    /// 校验并规范化工作目录（空值拒绝、相对路径转绝对路径）。
    /// </summary>
    /// <param name="workDirectory">原始工作目录。</param>
    /// <returns>规范化后的工作目录。</returns>
    private static string NormalizeWorkDirectory(string workDirectory)
    {
        if (string.IsNullOrWhiteSpace(workDirectory))
        {
            throw new ArgumentException("工作目录不能为空。", nameof(workDirectory));
        }

        return WorkspacePath.Normalize(workDirectory);
    }
}
