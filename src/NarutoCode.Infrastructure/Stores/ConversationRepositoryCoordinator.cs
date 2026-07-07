using System.Data.Common;
using System.Globalization;
using Microsoft.Agents.AI;
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
    /// <param name="inputTokenCount">本轮输入 Token 用量，用于压缩策略判断。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task AddAsync(
        long conversationId,
        List<ChatMessage> messages,
        long? totalUsage = null,
        long? inputTokenCount = null,
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
                transaction: null,
                conversationId,
                messages[index],
                cancellationToken);
        }
        //维护会话的token使用量
        if (totalUsage.GetValueOrDefault() > 0)
        {
            await AddConversationTokenCountAsync(
                connection,
                transaction: null,
                conversationId,
                totalUsage.GetValueOrDefault(),
                inputTokenCount.GetValueOrDefault(),
                cancellationToken);
        }
    }

    /// <summary>
    /// 在同一个事务中追加 UI 历史、更新 Token 用量并覆盖 LLM 运行时上下文。
    /// </summary>
    /// <param name="conversationId">对话 ID。</param>
    /// <param name="messages">待追加到 UI 历史的消息。</param>
    /// <param name="runtimeMessages">已裁剪的运行时上下文消息。</param>
    /// <param name="totalUsage">本轮总 Token 用量。</param>
    /// <param name="inputTokenCount">本轮输入 Token 用量，用于压缩策略判断。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task PersistHistoriesAsync(
        long conversationId,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatMessage> runtimeMessages,
        long? totalUsage = null,
        long? inputTokenCount = null,
        CancellationToken cancellationToken = default)
    {
        if (conversationId == 0)
        {
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var message in messages)
        {
            await InsertMessageAsync(
                connection,
                transaction,
                conversationId,
                message,
                cancellationToken);
        }

        if (totalUsage.GetValueOrDefault() > 0)
        {
            await AddConversationTokenCountAsync(
                connection,
                transaction,
                conversationId,
                totalUsage.GetValueOrDefault(),
                inputTokenCount.GetValueOrDefault(),
                cancellationToken);
        }

        await DeleteRuntimeMessagesAsync(
            connection,
            transaction,
            conversationId,
            cancellationToken);

        for (var index = 0; index < runtimeMessages.Count; index++)
        {
            await InsertRuntimeMessageAsync(
                connection,
                transaction,
                conversationId,
                index,
                runtimeMessages[index],
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 覆盖保存指定对话发送给 LLM 的运行时上下文消息。
    /// </summary>
    /// <param name="conversationId">对话 ID。</param>
    /// <param name="messages">已裁剪的运行时上下文消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task ReplaceRuntimeMessagesAsync(
        long conversationId,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (conversationId == 0)
        {
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await DeleteRuntimeMessagesAsync(
            connection,
            transaction,
            conversationId,
            cancellationToken);

        for (var index = 0; index < messages.Count; index++)
        {
            await InsertRuntimeMessageAsync(
                connection,
                transaction,
                conversationId,
                index,
                messages[index],
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task InsertMessageAsync(
        DbConnection connection,
        DbTransaction? transaction,
        long conversationId,
        ChatMessage chatMessage,
        CancellationToken cancellationToken)
    {
        var message = new Message
        {
            ConversationId = conversationId,
            Role = chatMessage.Role.Value,
#pragma warning disable MEAI001
            MessageType = GetMessageTypeWithChatMessage(chatMessage),
            Visibility = GetMessageVisibility(chatMessage),
            Content = chatMessage.Text,
            ModelContent = AIContentJsonSerializerContext.SerializeContents(chatMessage.Contents)
#pragma warning restore MEAI001
        };

        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO "Messages" ("Id", "ConversationId", "Role", "Content", "ModelContent", "CreatedAt", "ContentType", "MessageType", "Visibility")
            VALUES ($id, $conversationId, $role, $content, $modelContent, $createdAt, $contentType, $messageType, $visibility);
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
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 删除指定对话已有的 LLM 运行时上下文消息。
    /// </summary>
    /// <param name="connection">数据库连接。</param>
    /// <param name="transaction">当前事务。</param>
    /// <param name="conversationId">对话 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task DeleteRuntimeMessagesAsync(
        DbConnection connection,
        DbTransaction transaction,
        long conversationId,
        CancellationToken cancellationToken)
    {
        await using DbCommand deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText =
            """
            DELETE FROM "ConversationRuntimeMessages"
            WHERE "ConversationId" = $conversationId;
            """;
        AddParameter(deleteCommand, "$conversationId", conversationId);
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 插入一条发送给 LLM 的运行时上下文消息。
    /// </summary>
    /// <param name="connection">数据库连接。</param>
    /// <param name="transaction">当前覆盖写入事务。</param>
    /// <param name="conversationId">对话 ID。</param>
    /// <param name="sequence">运行时上下文顺序。</param>
    /// <param name="chatMessage">聊天消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task InsertRuntimeMessageAsync(
        DbConnection connection,
        DbTransaction transaction,
        long conversationId,
        int sequence,
        ChatMessage chatMessage,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO "ConversationRuntimeMessages" ("Id", "ConversationId", "Sequence", "Role", "ModelContent", "CreatedAt")
            VALUES ($id, $conversationId, $sequence, $role, $modelContent, $createdAt);
            """;
        AddParameter(command, "$id", SnowflakeIdHelper.Instance.NextId());
        AddParameter(command, "$conversationId", conversationId);
        AddParameter(command, "$sequence", sequence);
        AddParameter(command, "$role", chatMessage.Role.Value);
#pragma warning disable MEAI001
        AddParameter(command, "$modelContent", AIContentJsonSerializerContext.SerializeContents(chatMessage.Contents));
#pragma warning restore MEAI001
        AddParameter(command, "$createdAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 累加会话级 Token 用量并更新最近一次调用的输入 Token。
    /// </summary>
    /// <param name="connection">数据库连接。</param>
    /// <param name="transaction">当前事务。</param>
    /// <param name="conversationId">对话 ID。</param>
    /// <param name="tokenCount">本轮总 Token 用量。</param>
    /// <param name="inputTokenCount">本轮输入 Token 用量。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task AddConversationTokenCountAsync(
        DbConnection connection,
        DbTransaction? transaction,
        long conversationId,
        long tokenCount,
        long inputTokenCount,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE "Conversations"
            SET "TokenCount" = "TokenCount" + $tokenCount,
                "LastUsageTokenCount" = $tokenCount,
                "LastInputTokenCount" = $inputTokenCount
            WHERE "Id" = $conversationId;
            """;
        AddParameter(command, "$conversationId", conversationId);
        AddParameter(command, "$tokenCount", tokenCount);
        AddParameter(command, "$inputTokenCount", inputTokenCount);
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

    private static AgentMessageType GetMessageTypeWithChatMessage(ChatMessage message)
    {
        //AIContextProvider 的来源直接为临时消息
        if (message.AdditionalProperties != null
            && message.AdditionalProperties.TryGetValue(
                AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, out var messageSourceAttribution)
            && messageSourceAttribution is AgentRequestMessageSourceAttribution typedMessageSourceAttribution
            && typedMessageSourceAttribution.SourceType == AgentRequestMessageSourceType.AIContextProvider)
        {
            return AgentMessageType.Temporary;
        }

        return GetMessageType(message.Contents);
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
