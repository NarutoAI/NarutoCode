using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.AI;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Entities;
using NarutoCode.Domain.Messages;
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

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var existingConversation = await FindLatestConversationAsync(connection, workDirectory, cancellationToken);
        if (existingConversation is not null)
        {
            return existingConversation;
        }

        var now = DateTime.Now;
        var conversation = new Conversation
        {
            Title = CreateConversationTitle(workDirectory),
            WorkDirectory = workDirectory,
            CreatedAt = now,
            UpdatedAt = now
        };

        await InsertConversationAsync(connection, conversation, cancellationToken);
        return conversation;
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
                    Visibility = item.Visibility,
                    TokenCount = item.TokenCount
                });
            }
        }

        return resultList;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Message>> ListMessagesAsync(
        long conversationId,
        CancellationToken cancellationToken = default)
    {
        return await ListVisibleMessagesCoreAsync(
            conversationId,
            filterUiMessageTypes: false,
            cancellationToken);
    }

    private static async Task<Conversation?> FindLatestConversationAsync(
        DbConnection connection,
        string workDirectory,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT "Id", "Title", "CreatedAt", "UpdatedAt", "WorkDirectory"
            FROM "Conversations"
            WHERE "WorkDirectory" = $workDirectory
            ORDER BY "UpdatedAt" DESC
            LIMIT 1;
            """;
        AddParameter(command, "$workDirectory", workDirectory);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new Conversation
        {
            Id = reader.GetInt64(0),
            Title = reader.GetString(1),
            CreatedAt = ReadDateTime(reader, 2),
            UpdatedAt = ReadDateTime(reader, 3),
            WorkDirectory = reader.GetString(4)
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
            INSERT INTO "Conversations" ("Id", "Title", "CreatedAt", "UpdatedAt", "WorkDirectory")
            VALUES ($id, $title, $createdAt, $updatedAt, $workDirectory);
            """;
        AddParameter(command, "$id", conversation.Id);
        AddParameter(command, "$title", conversation.Title);
        AddParameter(command, "$createdAt", FormatDateTime(conversation.CreatedAt));
        AddParameter(command, "$updatedAt", FormatDateTime(conversation.UpdatedAt));
        AddParameter(command, "$workDirectory", conversation.WorkDirectory);
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
            SELECT "Id", "ConversationId", "Role", "Content", "ModelContent", "CreatedAt", "ContentType", "MessageType", "Visibility", "TokenCount"
            FROM "Messages"
            WHERE "ConversationId" = $conversationId
              AND "Visibility" = $visibility
              AND "MessageType" IN ($contentType, $thinkingType, $approvalType, $toolCallType,$errorType)
            ORDER BY "CreatedAt", "Id";
            """
            :
            """
            SELECT "Id", "ConversationId", "Role", "Content", "ModelContent", "CreatedAt", "ContentType", "MessageType", "Visibility", "TokenCount"
            FROM "Messages"
            WHERE "ConversationId" = $conversationId
              AND "Visibility" = $visibility
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
            Visibility = Enum.Parse<MessageVisibility>(reader.GetString(8)),
            TokenCount = reader.IsDBNull(9) ? null : reader.GetInt32(9)
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

    private static string CreateConversationTitle(string workDirectory)
    {
        var title = Path.GetFileName(workDirectory.TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(title) ? workDirectory : title;
    }
}
