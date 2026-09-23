using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Entities;
using NarutoCode.Domain.Enums;
using NarutoCode.Domain.Messages;
using NarutoCode.Domain.Configurations.Settings;
using NarutoCode.Domain.Workspaces;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// 基于 SQLite 的对话仓储实现（v2 snake_case 表结构），
/// 负责工作区、会话、UI 历史（agent_session_items）与 LLM 历史（agent_chat_messages）的持久化读写。
/// </summary>
/// <param name="connectionFactory">SQLite 连接工厂。</param>
/// <param name="llmSettingsService">LLM 运行时设置，会话创建时记录生效的 provider/model/effort。</param>
public sealed class ConversationRepository(
    SqliteConnectionFactory connectionFactory,
    ILlmSettingsService llmSettingsService) : IConversationRepository
{
    /// <inheritdoc />
    public async Task<Conversation> GetOrCreateByWorkDirectoryAsync(
        string workDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workDirectory))
        {
            throw new ArgumentException("工作目录不能为空。", nameof(workDirectory));
        }

        workDirectory = WorkspacePath.Normalize(workDirectory);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var projectId = await EnsureProjectAsync(connection, workDirectory, DateTime.Now, cancellationToken);
        var existingConversation = await FindLatestConversationAsync(connection, projectId, cancellationToken);
        if (existingConversation is not null)
        {
            return existingConversation;
        }

        return await CreateForProjectIdAsync(projectId, cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationSummary>> ListByWorkDirectoryAsync(
        string workDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workDirectory))
        {
            throw new ArgumentException("工作目录不能为空。", nameof(workDirectory));
        }

        workDirectory = WorkspacePath.Normalize(workDirectory);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // 会话列表与预览：UI 历史改由 agent_session_items 提供（userMessage kind 即真实用户输入）
        command.CommandText =
            """
            SELECT
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
            FROM agent_sessions c
            INNER JOIN agent_workspaces w ON w.id = c.workspace_id
            WHERE w.work_directory = $workDirectory
              AND c.source = $localSource
            ORDER BY c.updated_at DESC;
            """;
        AddParameter(command, "$workDirectory", workDirectory);
        AddParameter(command, "$localSource", (int)ConversationSource.Local);

        var summaries = new List<ConversationSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            summaries.Add(new ConversationSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                ReadDateTime(reader, 2),
                ReadDateTime(reader, 3),
                Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture),
                Convert.ToInt64(reader.GetValue(4), CultureInfo.InvariantCulture),
                Convert.ToInt64(reader.GetValue(5), CultureInfo.InvariantCulture),
                CreateMessagePreview(reader.IsDBNull(7) ? string.Empty : reader.GetString(7))));
        }

        return summaries;
    }

    /// <inheritdoc />
    public async Task<WorkspaceSummary> GetOrCreateWorkspaceAsync(
        string workDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workDirectory))
        {
            throw new ArgumentException("工作目录不能为空。", nameof(workDirectory));
        }

        workDirectory = WorkspacePath.Normalize(workDirectory);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var projectId = await EnsureProjectAsync(connection, workDirectory, DateTime.Now, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                w.id,
                w.name,
                w.work_directory,
                w.sort_order,
                w.created_at,
                w.updated_at,
                COALESCE(MAX(c.updated_at), w.updated_at) AS last_updated_at,
                COUNT(c.id) AS conversation_count
            FROM agent_workspaces w
            LEFT JOIN agent_sessions c ON c.workspace_id = w.id AND c.source = $localSource
            WHERE w.id = $workspaceId
            GROUP BY w.id, w.name, w.work_directory, w.sort_order, w.created_at, w.updated_at;
            """;
        AddParameter(command, "$workspaceId", projectId);
        AddParameter(command, "$localSource", (int)ConversationSource.Local);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"项目创建后无法读取：{workDirectory}");
        }

        return new WorkspaceSummary(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            ReadDateTime(reader, 4),
            ReadDateTime(reader, 5),
            ReadDateTime(reader, 6),
            Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture));
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

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // 会话列表与预览：UI 历史改由 agent_session_items 提供（userMessage kind 即真实用户输入）
        command.CommandText =
            """
            SELECT
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
            FROM agent_sessions c
            WHERE c.workspace_id = $workspaceId
              AND c.source = $localSource
            ORDER BY c.updated_at DESC;
            """;
        AddParameter(command, "$workspaceId", projectId);
        AddParameter(command, "$localSource", (int)ConversationSource.Local);

        var summaries = new List<ConversationSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            summaries.Add(new ConversationSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                ReadDateTime(reader, 2),
                ReadDateTime(reader, 3),
                Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture),
                Convert.ToInt64(reader.GetValue(4), CultureInfo.InvariantCulture),
                Convert.ToInt64(reader.GetValue(5), CultureInfo.InvariantCulture),
                CreateMessagePreview(ReadUserMessagePreview(reader.IsDBNull(7) ? string.Empty : reader.GetString(7)))));
        }

        return summaries;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkspaceSummary>> ListWorkspacesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                w.id,
                w.name,
                w.work_directory,
                w.sort_order,
                w.created_at,
                w.updated_at,
                COALESCE(MAX(c.updated_at), w.updated_at) AS last_updated_at,
                COUNT(c.id) AS conversation_count
            FROM agent_workspaces w
            LEFT JOIN agent_sessions c ON c.workspace_id = w.id AND c.source = $localSource
            GROUP BY w.id, w.name, w.work_directory, w.sort_order, w.created_at, w.updated_at
            ORDER BY w.sort_order, last_updated_at DESC, w.id;
            """;
        AddParameter(command, "$localSource", (int)ConversationSource.Local);

        var workspaces = new List<WorkspaceSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            workspaces.Add(new WorkspaceSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                ReadDateTime(reader, 4),
                ReadDateTime(reader, 5),
                ReadDateTime(reader, 6),
                Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture)));
        }

        return workspaces;
    }

    public async Task<Conversation> CreateForWorkDirectoryAsync(
        string workDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workDirectory))
        {
            throw new ArgumentException("工作目录不能为空。", nameof(workDirectory));
        }

        workDirectory = WorkspacePath.Normalize(workDirectory);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var projectId = await EnsureProjectAsync(connection, workDirectory, DateTime.Now, cancellationToken);
        return await CreateForProjectIdAsync(projectId, cancellationToken);
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

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        // 先查找该项目下指定来源类型与来源标识的最近会话
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, title, created_at, updated_at, workspace_id, work_directory,
                   token_count, last_usage_token_count, last_input_token_count, source, source_id,
                   llm_provider, llm_model, reasoning_effort
            FROM agent_sessions
            WHERE workspace_id = $workspaceId AND source = $source AND source_id = $sourceId
            ORDER BY updated_at DESC
            LIMIT 1;
            """;
        AddParameter(command, "$workspaceId", projectId);
        AddParameter(command, "$source", (int)source);
        AddParameter(command, "$sourceId", sourceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadConversation(reader);
        }

        // 不存在则创建指定来源与来源标识的会话
        return await CreateForProjectIdAsync(projectId, source, sourceId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Conversation> CreateForProjectIdAsync(
        long projectId,
        CancellationToken cancellationToken = default)
    {
        return await CreateForProjectIdAsync(projectId, ConversationSource.Local, string.Empty, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Conversation> CreateForProjectIdAsync(
        long projectId,
        ConversationSource source,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId), "项目标识必须大于零。");
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var workDirectory = await GetProjectWorkDirectoryAsync(connection, projectId, cancellationToken)
            ?? throw new InvalidOperationException($"项目不存在：{projectId}");
        var now = DateTime.Now;
        var llm = llmSettingsService.CurrentLlm;
        var conversation = new Conversation
        {
            ProjectId = projectId,
            Title = CreateConversationTitle(workDirectory),
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

        await InsertConversationAsync(connection, conversation, cancellationToken);
        await TouchProjectAsync(connection, projectId, now, cancellationToken);
        return conversation;
    }

    /// <inheritdoc />
    public async Task<Conversation?> GetByIdAsync(
        long conversationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, title, created_at, updated_at, workspace_id, work_directory,
                   token_count, last_usage_token_count, last_input_token_count, source, source_id,
                   llm_provider, llm_model, reasoning_effort
            FROM agent_sessions
            WHERE id = $conversationId
            LIMIT 1;
            """;
        AddParameter(command, "$conversationId", conversationId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadConversation(reader);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationHistoryMessage>> ListItemsAsync(
        long conversationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // UI 渲染历史 = agent_session_items（含完成态消息与用户交互问答卡片）
        command.CommandText =
            """
            SELECT kind, status, payload
            FROM agent_session_items
            WHERE session_id = $sessionId
            ORDER BY id;
            """;
        AddParameter(command, "$sessionId", conversationId);

        var messages = new List<ConversationHistoryMessage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var kind = reader.GetString(0);
            var status = reader.GetString(1);
            var payload = reader.GetString(2);
            var historyMessage = ProjectItemToHistoryMessage(kind, status, payload);
            if (historyMessage is not null)
            {
                messages.Add(historyMessage);
            }
        }

        return messages;
    }

    public async Task<IReadOnlyList<Message>> ListMessagesAsync(
        long conversationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // LLM 恢复用全量历史：排除 temporary（框架临时注入不参与恢复，等价旧版 Hidden 过滤）
        command.CommandText =
            """
            SELECT id, session_id, role, type, model_content, created_at
            FROM agent_chat_messages
            WHERE session_id = $sessionId
              AND type <> $temporary
            ORDER BY created_at, id;
            """;
        AddParameter(command, "$sessionId", conversationId);
        AddParameter(command, "$temporary", AgentMessageTypeNames.Temporary);

        var messages = new List<Message>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(ReadMessage(reader));
        }

        return messages;
    }

    public async Task<IReadOnlyList<Message>> ListRuntimeMessagesAsync(
        long conversationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // 运行时上下文按雪花 id 单调排序（替代旧版 Sequence 列）
        command.CommandText =
            """
            SELECT id, session_id, role, model_content, created_at
            FROM agent_chat_message_runtimes
            WHERE session_id = $sessionId
            ORDER BY id;
            """;
        AddParameter(command, "$sessionId", conversationId);

        var messages = new List<Message>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new Message
            {
                Id = reader.GetInt64(0),
                ConversationId = reader.GetInt64(1),
                Role = reader.GetString(2),
                ModelContent = reader.GetString(3),
                CreatedAt = ReadDateTime(reader, 4),
                Type = AgentMessageTypeNames.Content
            });
        }

        return messages;
    }

    /// <summary>
    /// 将 agent_session_items 行投影为 TUI 历史消息：
    /// 用户交互 pending → Content（问题）；completed/cancelled/expired → Content（问答摘要）；
    /// toolApprovalRequest（含审批 JSON）保持审批卡片，审批响应行与临时注入不渲染。
    /// </summary>
    /// <param name="kind">Item 类型。</param>
    /// <param name="status">Item 状态。</param>
    /// <param name="payload">Item 载荷 JSON。</param>
    /// <returns>历史消息；不需要渲染时返回 <see langword="null" />。</returns>
    private static ConversationHistoryMessage? ProjectItemToHistoryMessage(string kind, string status, string payload)
    {
        // 用户交互问答卡片：payload 为 { request, result } 复合结构
        if (string.Equals(kind, ConversationItemKinds.UserInteraction, StringComparison.Ordinal))
        {
            var interaction = UserInteractionJsonSerializerContext.DeserializeItemPayload(payload);
            if (interaction is null)
            {
                return null;
            }

            // 等待中的交互仅渲染问题，不泄露内部工具名
            var question = string.IsNullOrWhiteSpace(interaction.Request.Title)
                ? interaction.Request.Question
                : interaction.Request.Question;
            var content = interaction.Result is null
                ? $"❓ {question}"
                : $"❓ {question}{Environment.NewLine}↳ {interaction.Result.Value}";
            return CreateInteractionHistoryMessage(content);
        }

        // 消息类 Item：payload 直接携带 TUI 契约字段，按 kind 还原消息类型后重建历史消息
        var messagePayload = ConversationItemJsonSerializerContext.DeserializePayload(payload);
        if (messagePayload is null)
        {
            return null;
        }

        return new ConversationHistoryMessage(
            Enum.TryParse<ConversationMessageRole>(messagePayload.Role, ignoreCase: true, out var parsedRole)
                ? parsedRole
                : ConversationMessageRole.assistant,
            new AgentMessage(
                ConversationItemKinds.ToMessageType(kind),
                messagePayload.Content,
                messagePayload.ToolApprovalContent,
                messagePayload.CreatedAt));
    }

    /// <summary>
    /// 构建用户交互问答卡片对应的历史消息（角色固定为 assistant，内容为问答摘要文本）。
    /// </summary>
    /// <param name="content">问答摘要文本。</param>
    /// <returns>历史消息。</returns>
    private static ConversationHistoryMessage CreateInteractionHistoryMessage(string content)
    {
        return new ConversationHistoryMessage(
            ConversationMessageRole.assistant,
            new AgentMessage(
                AgentMessageType.Content,
                content,
                createdAt: DateTimeOffset.Now));
    }

    /// <summary>
    /// 确保工作目录存在对应项目记录；已有项目保留用户维护的名称和排序。
    /// </summary>
    private static async Task<long> EnsureProjectAsync(
        DbConnection connection,
        string workDirectory,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO agent_workspaces (name, work_directory, sort_order, created_at, updated_at)
                VALUES ($name, $workDirectory, 0, $createdAt, $updatedAt)
                ON CONFLICT(work_directory) DO NOTHING;
                """;
            AddParameter(command, "$name", CreateProjectName(workDirectory));
            AddParameter(command, "$workDirectory", workDirectory);
            AddParameter(command, "$createdAt", FormatDateTime(now));
            AddParameter(command, "$updatedAt", FormatDateTime(now));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var selectCommand = connection.CreateCommand();
        selectCommand.CommandText = "SELECT id FROM agent_workspaces WHERE work_directory = $workDirectory LIMIT 1;";
        AddParameter(selectCommand, "$workDirectory", workDirectory);
        var projectId = await selectCommand.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(projectId, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 将项目更新时间同步到新建会话时间，同时不修改项目名称和用户设置的排序值。
    /// </summary>
    private static async Task TouchProjectAsync(
        DbConnection connection,
        long projectId,
        DateTime updatedAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE agent_workspaces SET updated_at = $updatedAt WHERE id = $workspaceId;";
        AddParameter(command, "$updatedAt", FormatDateTime(updatedAt));
        AddParameter(command, "$workspaceId", projectId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 获取项目绑定的工作目录。
    /// </summary>
    private static async Task<string?> GetProjectWorkDirectoryAsync(
        DbConnection connection,
        long projectId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT work_directory FROM agent_workspaces WHERE id = $workspaceId LIMIT 1;";
        AddParameter(command, "$workspaceId", projectId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToString(result, CultureInfo.InvariantCulture);
    }

    private static async Task<Conversation?> FindLatestConversationAsync(
        DbConnection connection,
        long projectId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, title, created_at, updated_at, workspace_id, work_directory,
                   token_count, last_usage_token_count, last_input_token_count, source, source_id,
                   llm_provider, llm_model, reasoning_effort
            FROM agent_sessions
            WHERE workspace_id = $workspaceId
            ORDER BY updated_at DESC
            LIMIT 1;
            """;
        AddParameter(command, "$workspaceId", projectId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadConversation(reader);
    }

    private static Conversation ReadConversation(DbDataReader reader)
    {
        return new Conversation
        {
            Id = reader.GetInt64(0),
            Title = reader.GetString(1),
            CreatedAt = ReadDateTime(reader, 2),
            UpdatedAt = ReadDateTime(reader, 3),
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

    private static async Task InsertConversationAsync(
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
        AddParameter(command, "$id", conversation.Id);
        AddParameter(command, "$title", conversation.Title);
        AddParameter(command, "$createdAt", FormatDateTime(conversation.CreatedAt));
        AddParameter(command, "$updatedAt", FormatDateTime(conversation.UpdatedAt));
        AddParameter(command, "$workspaceId", conversation.ProjectId);
        AddParameter(command, "$workDirectory", conversation.WorkDirectory);
        AddParameter(command, "$tokenCount", conversation.TokenCount);
        AddParameter(command, "$lastUsageTokenCount", conversation.LastUsageTokenCount);
        AddParameter(command, "$lastInputTokenCount", conversation.LastInputTokenCount);
        AddParameter(command, "$source", (int)conversation.Source);
        AddParameter(command, "$sourceId", conversation.SourceId);
        AddParameter(command, "$llmProvider", conversation.LlmProvider);
        AddParameter(command, "$llmModel", conversation.LlmModel);
        AddParameter(command, "$reasoningEffort", conversation.ReasoningEffort);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Message ReadMessage(DbDataReader reader)
    {
        return new Message
        {
            Id = reader.GetInt64(0),
            ConversationId = reader.GetInt64(1),
            Role = reader.GetString(2),
            Type = reader.GetString(3),
            ModelContent = reader.GetString(4),
            CreatedAt = ReadDateTime(reader, 5)
        };
    }

    /// <summary>
    /// 从用户消息 Item 载荷中提取预览文本。
    /// </summary>
    /// <param name="payload">Item 载荷 JSON。</param>
    /// <returns>预览文本；解析失败返回空字符串。</returns>
    private static string ReadUserMessagePreview(string payload)
    {
        var messagePayload = ConversationItemJsonSerializerContext.DeserializePayload(payload);
        return messagePayload?.Content ?? string.Empty;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static DateTime ReadDateTime(DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value is DateTime dateTime
            ? dateTime
            : DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
    }

    private static string FormatDateTime(DateTime value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string CreateMessagePreview(string value)
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

    private static string CreateConversationTitle(string workDirectory) => CreateProjectName(workDirectory);

    /// <summary>
    /// 从工作目录生成项目默认名称。
    /// </summary>
    private static string CreateProjectName(string workDirectory)
    {
        var name = Path.GetFileName(workDirectory.TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? workDirectory : name;
    }
}
