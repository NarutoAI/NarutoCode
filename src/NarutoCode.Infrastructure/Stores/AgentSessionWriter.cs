using System.Data.Common;
using NarutoCode.Domain.Configurations.Settings;
using NarutoCode.Domain.Entities;
using NarutoCode.Domain.Enums;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// agent_sessions（会话）写入：创建会话记录（含创建时生效的 LLM 元数据）、累加 Token 用量。
/// 读取路径见 <see cref="AgentSessionReader" />。
/// </summary>
/// <param name="connectionFactory">SQLite 连接工厂。</param>
/// <param name="llmSettingsService">LLM 运行时设置，会话创建时记录生效的 provider/model/effort。</param>
public sealed class AgentSessionWriter(
    SqliteConnectionFactory connectionFactory,
    ILlmSettingsService llmSettingsService)
{
    /// <summary>
    /// 在工作区下创建会话：写入 agent_sessions 并同步工作区更新时间（同一连接内完成）。
    /// </summary>
    /// <param name="workspaceId">工作区主键。</param>
    /// <param name="source">会话来源类型。</param>
    /// <param name="sourceId">会话来源标识；本地会话为空字符串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建会话实体。</returns>
    /// <exception cref="InvalidOperationException">工作区不存在时抛出。</exception>
    public async Task<Conversation> CreateAsync(
        long workspaceId,
        ConversationSource source,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workspaceId), "工作区标识必须大于零。");
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        // 会话标题与工作目录取自工作区记录，工作区不存在说明调用方传入了非法主键
        var workDirectory = await AgentWorkspaceReader.GetWorkDirectoryAsync(connection, workspaceId, cancellationToken)
            ?? throw new InvalidOperationException($"工作区不存在：{workspaceId}");

        var now = DateTime.Now;
        var llm = llmSettingsService.CurrentLlm;
        var conversation = new Conversation
        {
            ProjectId = workspaceId,
            Title = AgentWorkspaceReader.CreateName(workDirectory),
            WorkDirectory = workDirectory,
            CreatedAt = now,
            UpdatedAt = now,
            Source = source,
            SourceId = sourceId,
            // 记录创建时生效的 LLM 元数据（提供商/模型/推理强度）
            LlmProvider = llm.Provider,
            LlmModel = llm.Model,
            ReasoningEffort = llmSettingsService.CurrentEffort.ToString().ToLowerInvariant()
        };

        await InsertAsync(connection, conversation, cancellationToken);
        await AgentWorkspaceWriter.TouchAsync(connection, workspaceId, now, cancellationToken);
        return conversation;
    }

    /// <summary>
    /// 在调用方连接 / 事务上累加会话 Token 用量并记录最近一次调用的输入 Token
    /// （供聊天消息写入在持久化事务内一并更新会话统计）。
    /// </summary>
    /// <param name="connection">已打开的数据库连接。</param>
    /// <param name="transaction">当前事务；无事务时为 <see langword="null" />。</param>
    /// <param name="conversationId">会话主键。</param>
    /// <param name="tokenCount">本轮累加的 Token 用量。</param>
    /// <param name="inputTokenCount">本轮输入 Token 用量，用于压缩策略判断。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    internal static async Task AddTokenUsageAsync(
        DbConnection connection,
        DbTransaction? transaction,
        long conversationId,
        long tokenCount,
        long inputTokenCount,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE agent_sessions
            SET token_count = token_count + $tokenCount,
                last_usage_token_count = $tokenCount,
                last_input_token_count = $inputTokenCount
            WHERE id = $sessionId;
            """;
        command.AddParameter("$sessionId", conversationId);
        command.AddParameter("$tokenCount", tokenCount);
        command.AddParameter("$inputTokenCount", inputTokenCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 插入一条会话记录。
    /// </summary>
    /// <param name="connection">已打开的数据库连接。</param>
    /// <param name="conversation">会话实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task InsertAsync(
        DbConnection connection,
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO agent_sessions
                (id, title, created_at, updated_at, workspace_id, work_directory,
                 token_count, last_usage_token_count, last_input_token_count, source, source_id,
                 llm_provider, llm_model, reasoning_effort)
            VALUES
                ($id, $title, $createdAt, $updatedAt, $workspaceId, $workDirectory,
                 $tokenCount, $lastUsageTokenCount, $lastInputTokenCount, $source, $sourceId,
                 $llmProvider, $llmModel, $reasoningEffort);
            """;
        command.AddParameter("$id", conversation.Id);
        command.AddParameter("$title", conversation.Title);
        command.AddParameter("$createdAt", conversation.CreatedAt.FormatDateTime());
        command.AddParameter("$updatedAt", conversation.UpdatedAt.FormatDateTime());
        command.AddParameter("$workspaceId", conversation.ProjectId);
        command.AddParameter("$workDirectory", conversation.WorkDirectory);
        command.AddParameter("$tokenCount", conversation.TokenCount);
        command.AddParameter("$lastUsageTokenCount", conversation.LastUsageTokenCount);
        command.AddParameter("$lastInputTokenCount", conversation.LastInputTokenCount);
        command.AddParameter("$source", (int)conversation.Source);
        command.AddParameter("$sourceId", conversation.SourceId);
        command.AddParameter("$llmProvider", conversation.LlmProvider);
        command.AddParameter("$llmModel", conversation.LlmModel);
        command.AddParameter("$reasoningEffort", conversation.ReasoningEffort);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
