using System.Data.Common;
using System.Globalization;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Entities;
using NarutoCode.Domain.Enums;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// agent_sessions（会话）只读访问：按主键 / 工作区 / 来源定位会话，以及入口页会话摘要列表。
/// 写入路径见 <see cref="AgentSessionWriter" />。
/// </summary>
/// <param name="connectionFactory">SQLite 连接工厂。</param>
public sealed class AgentSessionReader(SqliteConnectionFactory connectionFactory)
{
    /// <summary>会话实体查询列（顺序与 <see cref="ReadConversation" /> 一致）。</summary>
    private const string ConversationColumns =
        "id, title, created_at, updated_at, workspace_id, work_directory, " +
        "token_count, last_usage_token_count, last_input_token_count, source, source_id, " +
        "llm_provider, llm_model, reasoning_effort";

    /// <summary>
    /// 按主键读取会话实体。
    /// </summary>
    /// <param name="conversationId">会话主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>会话实体；不存在时返回 <see langword="null" />。</returns>
    public async Task<Conversation?> GetByIdAsync(long conversationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {ConversationColumns}
            FROM agent_sessions
            WHERE id = $conversationId
            LIMIT 1;
            """;
        command.AddParameter("$conversationId", conversationId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadConversation(reader) : null;
    }

    /// <summary>
    /// 读取工作区下最近更新的会话（不限来源类型）。
    /// </summary>
    /// <param name="workspaceId">工作区主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>会话实体；不存在时返回 <see langword="null" />。</returns>
    public async Task<Conversation?> FindLatestAsync(long workspaceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {ConversationColumns}
            FROM agent_sessions
            WHERE workspace_id = $workspaceId
            ORDER BY updated_at DESC
            LIMIT 1;
            """;
        command.AddParameter("$workspaceId", workspaceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadConversation(reader) : null;
    }

    /// <summary>
    /// 按来源类型与来源标识读取工作区下的最近会话（网关按通道查询对应会话）。
    /// </summary>
    /// <param name="workspaceId">工作区主键。</param>
    /// <param name="source">会话来源类型。</param>
    /// <param name="sourceId">会话来源标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>会话实体；不存在时返回 <see langword="null" />。</returns>
    public async Task<Conversation?> FindBySourceAsync(
        long workspaceId,
        ConversationSource source,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {ConversationColumns}
            FROM agent_sessions
            WHERE workspace_id = $workspaceId AND source = $source AND source_id = $sourceId
            ORDER BY updated_at DESC
            LIMIT 1;
            """;
        command.AddParameter("$workspaceId", workspaceId);
        command.AddParameter("$source", (int)source);
        command.AddParameter("$sourceId", sourceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadConversation(reader) : null;
    }

    /// <summary>
    /// 按工作目录列出会话摘要（按最近更新倒序，仅本地来源）。
    /// </summary>
    /// <param name="workDirectory">规范化后的工作目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>会话摘要集合。</returns>
    public async Task<IReadOnlyList<ConversationSummary>> ListByWorkDirectoryAsync(
        string workDirectory,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $$"""
            SELECT {{SummaryColumns}}
            FROM agent_sessions c
            INNER JOIN agent_workspaces w ON w.id = c.workspace_id
            WHERE w.work_directory = $workDirectory
              AND c.source = $localSource
            ORDER BY c.updated_at DESC;
            """;
        command.AddParameter("$workDirectory", workDirectory);
        command.AddParameter("$localSource", (int)ConversationSource.Local);

        return await ReadSummariesAsync(command, cancellationToken);
    }

    /// <summary>
    /// 按工作区主键列出会话摘要（按最近更新倒序，仅本地来源）。
    /// </summary>
    /// <param name="workspaceId">工作区主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>会话摘要集合。</returns>
    public async Task<IReadOnlyList<ConversationSummary>> ListByWorkspaceIdAsync(
        long workspaceId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $$"""
            SELECT {{SummaryColumns}}
            FROM agent_sessions c
            WHERE c.workspace_id = $workspaceId
              AND c.source = $localSource
            ORDER BY c.updated_at DESC;
            """;
        command.AddParameter("$workspaceId", workspaceId);
        command.AddParameter("$localSource", (int)ConversationSource.Local);

        return await ReadSummariesAsync(command, cancellationToken);
    }

    /// <summary>
    /// 会话摘要查询列：消息数与最后一条用户消息预览取自 agent_session_items。
    /// </summary>
    private const string SummaryColumns =
        """
        c.id,
        c.title,
        c.created_at,
        c.updated_at,
        c.token_count,
        c.last_usage_token_count,
        (SELECT COUNT(*) FROM agent_session_items i
         WHERE i.session_id = c.id) AS message_count,
        COALESCE((
            SELECT p.payload
            FROM agent_session_items p
            WHERE p.session_id = c.id
              AND p.kind = 'userMessage'
            ORDER BY p.id DESC
            LIMIT 1
        ), '') AS last_user_message_payload
        """;

    /// <summary>
    /// 执行会话摘要查询并逐行投影。
    /// </summary>
    /// <param name="command">已配置好 SQL 与参数的查询命令。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>会话摘要集合。</returns>
    private static async Task<IReadOnlyList<ConversationSummary>> ReadSummariesAsync(
        DbCommand command,
        CancellationToken cancellationToken)
    {
        var summaries = new List<ConversationSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // UI 历史预览取自 items.payload（userMessage 载荷），需反序列化后取文本再截断
            var previewPayload = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
            summaries.Add(new ConversationSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.ReadDateTime(2),
                reader.ReadDateTime(3),
                Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture),
                Convert.ToInt64(reader.GetValue(4), CultureInfo.InvariantCulture),
                Convert.ToInt64(reader.GetValue(5), CultureInfo.InvariantCulture),
                CreateMessagePreview(AgentSessionItemProjector.ReadUserMessagePreview(previewPayload))));
        }

        return summaries;
    }

    /// <summary>
    /// 将查询行读取为会话实体（列顺序与 <see cref="ConversationColumns" /> 一致）。
    /// </summary>
    /// <param name="reader">数据读取器。</param>
    /// <returns>会话实体。</returns>
    internal static Conversation ReadConversation(DbDataReader reader)
    {
        return new Conversation
        {
            Id = reader.GetInt64(0),
            Title = reader.GetString(1),
            CreatedAt = reader.ReadDateTime(2),
            UpdatedAt = reader.ReadDateTime(3),
            ProjectId = reader.GetInt64(4),
            WorkDirectory = reader.GetString(5),
            TokenCount = reader.GetInt64(6),
            LastUsageTokenCount = reader.GetInt64(7),
            LastInputTokenCount = reader.GetInt64(8),
            Source = (ConversationSource)reader.GetInt32(9),
            SourceId = reader.GetString(10),
            LlmProvider = reader.GetString(11),
            LlmModel = reader.GetString(12),
            ReasoningEffort = reader.GetString(13)
        };
    }

    /// <summary>
    /// 生成单行消息预览：折叠换行并截断到固定长度。
    /// </summary>
    /// <param name="value">原始文本。</param>
    /// <returns>预览文本。</returns>
    internal static string CreateMessagePreview(string value)
    {
        const int maxPreviewLength = 80;
        var preview = value
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Trim();
        return preview.Length <= maxPreviewLength
            ? preview
            : string.Concat(preview.AsSpan(0, maxPreviewLength), "…");
    }
}
