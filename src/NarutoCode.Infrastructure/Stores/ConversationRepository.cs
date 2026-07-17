using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.AI;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Entities;
using NarutoCode.Domain.Messages;
using NarutoCode.Domain.Workspaces;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// 基于 SQLite 的对话仓储实现，负责本地会话与消息持久化。
/// </summary>
public sealed class ConversationRepository(SqliteConnectionFactory connectionFactory) : IConversationRepository
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
        command.CommandText =
            """
            SELECT
                c."Id",
                c."Title",
                c."CreatedAt",
                c."UpdatedAt",
                c."TokenCount",
                c."LastUsageTokenCount",
                COUNT(m."Id") AS "MessageCount",
                COALESCE((
                    SELECT um."Content"
                    FROM "Messages" um
                    WHERE um."ConversationId" = c."Id"
                      AND um."Role" = 'user'
                      AND um."Visibility" = $visibility
                    ORDER BY um."CreatedAt" DESC, um."Id" DESC
                    LIMIT 1
                ), '') AS "LastUserMessagePreview"
            FROM "Conversations" c
            INNER JOIN "Projects" p ON p."Id" = c."ProjectId"
            LEFT JOIN "Messages" m
                ON m."ConversationId" = c."Id"
               AND m."Visibility" = $visibility
            WHERE p."WorkDirectory" = $workDirectory
            GROUP BY c."Id", c."Title", c."CreatedAt", c."UpdatedAt", c."TokenCount", c."LastUsageTokenCount"
            ORDER BY c."UpdatedAt" DESC;
            """;
        AddParameter(command, "$workDirectory", workDirectory);
        AddParameter(command, "$visibility", MessageVisibility.Visible.ToString());

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
                CreateMessagePreview(reader.GetString(7))));
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
                p."Id",
                p."Name",
                p."WorkDirectory",
                p."SortOrder",
                p."CreatedAt",
                p."UpdatedAt",
                COALESCE(MAX(c."UpdatedAt"), p."UpdatedAt") AS "LastUpdatedAt",
                COUNT(c."Id") AS "ConversationCount"
            FROM "Projects" p
            LEFT JOIN "Conversations" c ON c."ProjectId" = p."Id"
            WHERE p."Id" = $projectId
            GROUP BY p."Id", p."Name", p."WorkDirectory", p."SortOrder", p."CreatedAt", p."UpdatedAt";
            """;
        AddParameter(command, "$projectId", projectId);

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
        command.CommandText =
            """
            SELECT
                c."Id",
                c."Title",
                c."CreatedAt",
                c."UpdatedAt",
                c."TokenCount",
                c."LastUsageTokenCount",
                COUNT(m."Id") AS "MessageCount",
                COALESCE((
                    SELECT um."Content"
                    FROM "Messages" um
                    WHERE um."ConversationId" = c."Id"
                      AND um."Role" = 'user'
                      AND um."Visibility" = $visibility
                    ORDER BY um."CreatedAt" DESC, um."Id" DESC
                    LIMIT 1
                ), '') AS "LastUserMessagePreview"
            FROM "Conversations" c
            LEFT JOIN "Messages" m
                ON m."ConversationId" = c."Id"
               AND m."Visibility" = $visibility
            WHERE c."ProjectId" = $projectId
            GROUP BY c."Id", c."Title", c."CreatedAt", c."UpdatedAt", c."TokenCount", c."LastUsageTokenCount"
            ORDER BY c."UpdatedAt" DESC;
            """;
        AddParameter(command, "$projectId", projectId);
        AddParameter(command, "$visibility", MessageVisibility.Visible.ToString());

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
                CreateMessagePreview(reader.GetString(7))));
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
                p."Id",
                p."Name",
                p."WorkDirectory",
                p."SortOrder",
                p."CreatedAt",
                p."UpdatedAt",
                COALESCE(MAX(c."UpdatedAt"), p."UpdatedAt") AS "LastUpdatedAt",
                COUNT(c."Id") AS "ConversationCount"
            FROM "Projects" p
            LEFT JOIN "Conversations" c ON c."ProjectId" = p."Id"
            GROUP BY p."Id", p."Name", p."WorkDirectory", p."SortOrder", p."CreatedAt", p."UpdatedAt"
            ORDER BY p."SortOrder", "LastUpdatedAt" DESC, p."Id";
            """;

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
    public async Task<Conversation> CreateForProjectIdAsync(
        long projectId,
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
        var conversation = new Conversation
        {
            ProjectId = projectId,
            Title = CreateConversationTitle(workDirectory),
            WorkDirectory = workDirectory,
            CreatedAt = now,
            UpdatedAt = now
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
            SELECT "Id", "Title", "CreatedAt", "UpdatedAt", "ProjectId", "WorkDirectory", "TokenCount", "LastUsageTokenCount", "LastInputTokenCount"
            FROM "Conversations"
            WHERE "Id" = $conversationId
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

    /// <summary>
    /// 获取用于 UI 展示的可见消息。
    /// </summary>
    /// <param name="conversationId">对话 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>UI 消息列表。</returns>
    public async Task<IReadOnlyList<Message>> ListMessagesWithUIAsync(
        long conversationId,
        CancellationToken cancellationToken = default)
    {
        var messages = await ListVisibleMessagesCoreAsync(
            conversationId,
            filterUiMessageTypes: true,
            cancellationToken);

        var resultList = new List<Message>();
        foreach (var item in messages)
        {
            var contents = AIContentJsonSerializerContext.DeserializeContents(item.ModelContent);
            var modelContent = string.Empty;
            foreach (var itemContent in contents)
            {
                var messageType = AgentMessageType.Content;
                var content = string.Empty;
                if (itemContent is TextContent textContent)
                {
                    content = textContent.Text;
                }
                else if (itemContent is FunctionCallContent functionCallContent)
                {
                    messageType = AgentMessageType.ToolCall;
                    content = functionCallContent.Name;
                }
                else if (itemContent is ToolApprovalRequestContent
                         {
                             ToolCall: FunctionCallContent functionCallContentApproval
                         } toolApprovalRequestContent)
                {
                    messageType = AgentMessageType.ToolApprovalRequest;
                    content =
                        $"{functionCallContentApproval.Name}({string.Join(',', functionCallContentApproval.Arguments ?? new Dictionary<string, object?>())})";
                    modelContent =
                        AIContentJsonSerializerContext.SerializeToolApprovalRequestContent(toolApprovalRequestContent);
                }
                else if (itemContent is TextReasoningContent textReasoningContent)
                {
                    messageType = AgentMessageType.Thinking;
                    content = textReasoningContent.Text;
                }
                else if (itemContent is ErrorContent errorContent)
                {
                    messageType = AgentMessageType.Error;
                    content = errorContent.Message;
                }
                else
                {
                    continue;
                }

                resultList.Add(new Message
                {
                    Id = item.Id,
                    ConversationId = item.ConversationId,
                    Role = item.Role,
                    Content = content,
                    ModelContent = modelContent,
                    CreatedAt = item.CreatedAt,
                    ContentType = item.ContentType,
                    MessageType = messageType,
                    Visibility = item.Visibility
                });
            }
        }

        return resultList;
    }

  
    public async Task<IReadOnlyList<Message>> ListMessagesAsync(
        long conversationId,
        CancellationToken cancellationToken = default)
    {
        return await ListVisibleMessagesCoreAsync(
            conversationId,
            filterUiMessageTypes: false,
            cancellationToken);
    }
    
    public async Task<IReadOnlyList<Message>> ListRuntimeMessagesAsync(
        long conversationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT "Id", "ConversationId", "Role", "ModelContent", "CreatedAt"
            FROM "ConversationRuntimeMessages"
            WHERE "ConversationId" = $conversationId
            ORDER BY "Sequence", "Id";
            """;
        AddParameter(command, "$conversationId", conversationId);

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
                Content = string.Empty,
                ContentType = string.Empty,
                MessageType = AgentMessageType.Content,
                Visibility = MessageVisibility.Visible
            });
        }

        return messages;
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
                INSERT INTO "Projects" ("Name", "WorkDirectory", "SortOrder", "CreatedAt", "UpdatedAt")
                VALUES ($name, $workDirectory, 0, $createdAt, $updatedAt)
                ON CONFLICT("WorkDirectory") DO NOTHING;
                """;
            AddParameter(command, "$name", CreateProjectName(workDirectory));
            AddParameter(command, "$workDirectory", workDirectory);
            AddParameter(command, "$createdAt", FormatDateTime(now));
            AddParameter(command, "$updatedAt", FormatDateTime(now));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var selectCommand = connection.CreateCommand();
        selectCommand.CommandText = "SELECT \"Id\" FROM \"Projects\" WHERE \"WorkDirectory\" = $workDirectory LIMIT 1;";
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
        command.CommandText =
            "UPDATE \"Projects\" SET \"UpdatedAt\" = $updatedAt WHERE \"Id\" = $projectId;";
        AddParameter(command, "$updatedAt", FormatDateTime(updatedAt));
        AddParameter(command, "$projectId", projectId);
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
        command.CommandText = "SELECT \"WorkDirectory\" FROM \"Projects\" WHERE \"Id\" = $projectId LIMIT 1;";
        AddParameter(command, "$projectId", projectId);
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
            SELECT "Id", "Title", "CreatedAt", "UpdatedAt", "ProjectId", "WorkDirectory", "TokenCount", "LastUsageTokenCount", "LastInputTokenCount"
            FROM "Conversations"
            WHERE "ProjectId" = $projectId
            ORDER BY "UpdatedAt" DESC
            LIMIT 1;
            """;
        AddParameter(command, "$projectId", projectId);

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
            LastInputTokenCount = reader.GetInt64(8)
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
            INSERT INTO "Conversations" ("Id", "Title", "CreatedAt", "UpdatedAt", "ProjectId", "WorkDirectory", "TokenCount", "LastUsageTokenCount", "LastInputTokenCount")
            VALUES ($id, $title, $createdAt, $updatedAt, $projectId, $workDirectory, $tokenCount, $lastUsageTokenCount, $lastInputTokenCount);
            """;
        AddParameter(command, "$id", conversation.Id);
        AddParameter(command, "$title", conversation.Title);
        AddParameter(command, "$createdAt", FormatDateTime(conversation.CreatedAt));
        AddParameter(command, "$updatedAt", FormatDateTime(conversation.UpdatedAt));
        AddParameter(command, "$projectId", conversation.ProjectId);
        AddParameter(command, "$workDirectory", conversation.WorkDirectory);
        AddParameter(command, "$tokenCount", conversation.TokenCount);
        AddParameter(command, "$lastUsageTokenCount", conversation.LastUsageTokenCount);
        AddParameter(command, "$lastInputTokenCount", conversation.LastInputTokenCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<Message>> ListVisibleMessagesCoreAsync(
        long conversationId,
        bool filterUiMessageTypes,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = filterUiMessageTypes
            ?
            """
            SELECT "Id", "ConversationId", "Role", "Content", "ModelContent", "CreatedAt", "ContentType", "MessageType", "Visibility"
            FROM "Messages"
            WHERE "ConversationId" = $conversationId 
              AND "Visibility" = $visibility
              AND "MessageType" IN ($contentType, $thinkingType, $approvalType, $toolCallType,$errorType)
            ORDER BY "CreatedAt", "Id";
            """
            :
            """
            SELECT "Id", "ConversationId", "Role", "Content", "ModelContent", "CreatedAt", "ContentType", "MessageType", "Visibility"
            FROM "Messages"
            WHERE "ConversationId" = $conversationId
              AND "Visibility" = $visibility
             AND "MessageType"!= $temporary
            ORDER BY "CreatedAt", "Id";
            """;

        AddParameter(command, "$conversationId", conversationId);
        AddParameter(command, "$visibility", MessageVisibility.Visible.ToString());
        if (filterUiMessageTypes)
        {
            AddParameter(command, "$contentType", (int) AgentMessageType.Content);
            AddParameter(command, "$thinkingType", (int) AgentMessageType.Thinking);
            AddParameter(command, "$approvalType", (int) AgentMessageType.ToolApprovalRequest);
            AddParameter(command, "$toolCallType", (int) AgentMessageType.ToolCall);
            AddParameter(command, "$errorType", (int) AgentMessageType.Error);
        }
        else
        {
            AddParameter(command, "temporary", (int) AgentMessageType.Temporary);
        }

        var messages = new List<Message>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(ReadMessage(reader));
        }

        return messages;
    }

    private static Message ReadMessage(DbDataReader reader)
    {
        return new Message
        {
            Id = reader.GetInt64(0),
            ConversationId = reader.GetInt64(1),
            Role = reader.GetString(2),
            Content = reader.GetString(3),
            ModelContent = reader.GetString(4),
            CreatedAt = ReadDateTime(reader, 5),
            ContentType = reader.GetString(6),
            MessageType = (AgentMessageType) reader.GetInt32(7),
            Visibility = Enum.Parse<MessageVisibility>(reader.GetString(8))
        };
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
