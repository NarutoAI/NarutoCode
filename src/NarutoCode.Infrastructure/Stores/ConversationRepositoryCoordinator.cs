using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.AI;
using NarutoCode.Domain;
using NarutoCode.Domain.Entities;
using NarutoCode.Domain.Messages;
using NarutoCode.Infrastructure.AIAgents;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// 负责将 Agent 消息批量写入本地会话数据库。
/// </summary>
public class ConversationRepositoryCoordinator(SqliteConnectionFactory connectionFactory)
{
    /// <summary>
    /// 批量追加对话消息。
    /// </summary>
    /// <param name="conversationId">对话 ID。</param>
    /// <param name="messages">待写入消息。</param>
    /// <param name="totalUsage">本轮总 Token 用量。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task AddAsync(
        long conversationId,
        List<ChatMessage> messages,
        long? totalUsage = null,
        CancellationToken cancellationToken = default)
    {
        if (conversationId == 0 || messages.Count == 0)
        {
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        for (var index = 0; index < messages.Count; index++)
        {
            await InsertMessageAsync(
                connection,
                conversationId,
                messages[index],
                index == 0 ? (int) totalUsage.GetValueOrDefault() : 0,
                cancellationToken);
        }
    }

    private static async Task InsertMessageAsync(
        DbConnection connection,
        long conversationId,
        ChatMessage chatMessage,
        int tokenCount,
        CancellationToken cancellationToken)
    {
        var message = new Message
        {
            ConversationId = conversationId,
            Role = chatMessage.Role.Value,
#pragma warning disable MEAI001
            MessageType = GetMessageType(chatMessage.Contents),
            Visibility = GetMessageVisibility(chatMessage),
            Content = chatMessage.Text,
            TokenCount = tokenCount,
            ModelContent = AIContentJsonSerializerContext.SerializeContents(chatMessage.Contents)
#pragma warning restore MEAI001
        };

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Messages" ("Id", "ConversationId", "Role", "Content", "ModelContent", "CreatedAt", "ContentType", "MessageType", "Visibility", "TokenCount")
            VALUES ($id, $conversationId, $role, $content, $modelContent, $createdAt, $contentType, $messageType, $visibility, $tokenCount);
            """;
        AddParameter(command, "$id", message.Id);
        AddParameter(command, "$conversationId", message.ConversationId);
        AddParameter(command, "$role", message.Role);
        AddParameter(command, "$content", message.Content);
        AddParameter(command, "$modelContent", message.ModelContent);
        AddParameter(command, "$createdAt", message.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        AddParameter(command, "$contentType", message.ContentType);
        AddParameter(command, "$messageType", (int) message.MessageType);
        AddParameter(command, "$visibility", message.Visibility.ToString());
        AddParameter(command, "$tokenCount", message.TokenCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// 根据聊天消息扩展属性判断消息可见性。
    /// </summary>
    /// <param name="message">聊天消息。</param>
    /// <returns>消息可见性。</returns>
    private static MessageVisibility GetMessageVisibility(ChatMessage message)
    {
        // 非用户消息允许显示；用户消息仅显示真实用户输入，隐藏框架补充的上下文消息。
        if (!string.Equals(message.Role.Value, ChatRole.User.Value, StringComparison.OrdinalIgnoreCase))
        {
            return MessageVisibility.Visible;
        }

        return TryReadBooleanProperty(message, ChatMessageAdditionalPropertyNames.IsUserInput, out var isUserInput) &&
               isUserInput
            ? MessageVisibility.Visible
            : MessageVisibility.Hidden;
    }

    /// <summary>
    /// 读取布尔类型的聊天消息扩展属性。
    /// </summary>
    /// <param name="message">聊天消息。</param>
    /// <param name="propertyName">扩展属性名称。</param>
    /// <param name="value">扩展属性值。</param>
    /// <returns>成功读取布尔值时返回 true。</returns>
    private static bool TryReadBooleanProperty(ChatMessage message, string propertyName, out bool value)
    {
        value = false;
        if (message.AdditionalProperties?.TryGetValue(propertyName, out var propertyValue) != true)
        {
            return false;
        }

        if (propertyValue is bool booleanValue)
        {
            value = booleanValue;
            return true;
        }

        return propertyValue is string stringValue && bool.TryParse(stringValue, out value);
    }

    /// <summary>
    /// 根据 AI 内容集合判断消息类型。
    /// </summary>
    /// <param name="contents">AI 内容集合。</param>
    /// <returns>消息类型。</returns>
    private static AgentMessageType GetMessageType(IList<AIContent> contents)
    {
        if (contents is not {Count: > 0})
        {
            return AgentMessageType.Content;
        }

        if (contents.OfType<TextReasoningContent>().Any())
        {
            return AgentMessageType.Thinking;
        }

        if (contents.OfType<ToolApprovalRequestContent>().Any())
        {
            return AgentMessageType.ToolApprovalRequest;
        }

        if (contents.OfType<ToolApprovalResponseContent>().Any())
        {
            return AgentMessageType.ToolApprovalResponse;
        }

        if (contents.OfType<FunctionCallContent>().Any())
        {
            return AgentMessageType.ToolCall;
        }

        if (contents.OfType<ErrorContent>().Any())
        {
            return AgentMessageType.Error;
        }

        return AgentMessageType.Content;
    }
}
