using System.Data.Common;
using System.Globalization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NarutoCode.Domain;
using NarutoCode.Domain.Messages;
using NarutoCode.Infrastructure.AIAgents;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// agent_chat_messages（append-only 全量聊天历史）与 agent_chat_message_runtimes（覆盖式运行时上下文）写入：
/// 面向模型的持久化通道（LLM 恢复 + 审计），由聊天历史持久化链路驱动。
/// 面向 UI 的 <c>agent_session_items</c> 由 <see cref="AgentSessionItemWriter" /> 独立写入，二者职责分离。
/// 读取路径见 <see cref="AgentChatMessageReader" />。
/// </summary>
/// <param name="connectionFactory">SQLite 连接工厂。</param>
public sealed class AgentChatMessageWriter(SqliteConnectionFactory connectionFactory)
{
    /// <summary>
    /// 批量追加聊天消息（不写运行时上下文、不产生 UI Item）。
    /// </summary>
    /// <param name="sessionId">会话主键。</param>
    /// <param name="messages">待写入消息。</param>
    /// <param name="totalUsage">本轮总 Token 用量。</param>
    /// <param name="inputTokenCount">本轮输入 Token 用量，用于压缩策略判断。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task AddAsync(
        long sessionId,
        List<ChatMessage> messages,
        long? totalUsage = null,
        long? inputTokenCount = null,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == 0 || messages.Count == 0)
        {
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        foreach (var message in messages)
        {
            await InsertMessageAsync(connection, transaction: null, sessionId, message, cancellationToken);
        }

        // 维护会话的 Token 使用量
        if (totalUsage.GetValueOrDefault() > 0)
        {
            await AgentSessionWriter.AddTokenUsageAsync(
                connection,
                transaction: null,
                sessionId,
                totalUsage.GetValueOrDefault(),
                inputTokenCount.GetValueOrDefault(),
                cancellationToken);
        }
    }

    /// <summary>
    /// 在同一事务中追加聊天消息、更新 Token 用量并覆盖运行时上下文。
    /// </summary>
    /// <param name="sessionId">会话主键。</param>
    /// <param name="messages">待追加到历史的增量消息。</param>
    /// <param name="runtimeMessages">已裁剪的运行时上下文消息。</param>
    /// <param name="totalUsage">本轮总 Token 用量。</param>
    /// <param name="inputTokenCount">本轮输入 Token 用量，用于压缩策略判断。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task PersistHistoriesAsync(
        long sessionId,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatMessage> runtimeMessages,
        long? totalUsage = null,
        long? inputTokenCount = null,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == 0)
        {
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var message in messages)
        {
            await InsertMessageAsync(connection, transaction, sessionId, message, cancellationToken);
        }

        if (totalUsage.GetValueOrDefault() > 0)
        {
            await AgentSessionWriter.AddTokenUsageAsync(
                connection,
                transaction,
                sessionId,
                totalUsage.GetValueOrDefault(),
                inputTokenCount.GetValueOrDefault(),
                cancellationToken);
        }

        // 运行时上下文为覆盖式：先清空再写入本轮裁剪结果
        await DeleteRuntimeMessagesAsync(connection, transaction, sessionId, cancellationToken);

        foreach (var runtimeMessage in runtimeMessages)
        {
            await InsertRuntimeMessageAsync(connection, transaction, sessionId, runtimeMessage, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 覆盖保存指定会话发送给 LLM 的运行时上下文消息。
    /// </summary>
    /// <param name="sessionId">会话主键。</param>
    /// <param name="messages">已裁剪的运行时上下文消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task ReplaceRuntimeMessagesAsync(
        long sessionId,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == 0)
        {
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await DeleteRuntimeMessagesAsync(connection, transaction, sessionId, cancellationToken);

        foreach (var message in messages)
        {
            await InsertRuntimeMessageAsync(connection, transaction, sessionId, message, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 写入一条聊天消息（agent_chat_messages 四列，append-only）。
    /// </summary>
    /// <param name="connection">已打开的数据库连接。</param>
    /// <param name="transaction">当前事务；无事务时为 <see langword="null" />。</param>
    /// <param name="sessionId">会话主键。</param>
    /// <param name="chatMessage">聊天消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task InsertMessageAsync(
        DbConnection connection,
        DbTransaction? transaction,
        long sessionId,
        ChatMessage chatMessage,
        CancellationToken cancellationToken)
    {
        // 类型判定：AIContextProvider 注入或非真实用户输入 → temporary（不参与 LLM 恢复）
        var isTemporary = IsTemporaryMessage(chatMessage);
        var messageType = isTemporary ? AgentMessageType.Temporary : GetMessageType(chatMessage.Contents);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO agent_chat_messages (id, session_id, role, type, model_content, created_at)
            VALUES ($id, $sessionId, $role, $type, $modelContent, $createdAt);
            """;
        command.AddParameter("$id", SnowflakeIdHelper.Instance.NextId());
        command.AddParameter("$sessionId", sessionId);
        command.AddParameter("$role", chatMessage.Role.Value);
#pragma warning disable MEAI001
        command.AddParameter("$type", AgentMessageTypeNames.ToName(messageType));
        command.AddParameter("$modelContent", AIContentJsonSerializerContext.SerializeContents(chatMessage.Contents));
#pragma warning restore MEAI001
        command.AddParameter("$createdAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 删除指定会话已有的运行时上下文消息（覆盖式写入的前置步骤）。
    /// </summary>
    /// <param name="connection">已打开的数据库连接。</param>
    /// <param name="transaction">当前事务。</param>
    /// <param name="sessionId">会话主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task DeleteRuntimeMessagesAsync(
        DbConnection connection,
        DbTransaction transaction,
        long sessionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM agent_chat_message_runtimes WHERE session_id = $sessionId;";
        command.AddParameter("$sessionId", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 插入一条发送给 LLM 的运行时上下文消息。
    /// </summary>
    /// <param name="connection">已打开的数据库连接。</param>
    /// <param name="transaction">当前事务。</param>
    /// <param name="sessionId">会话主键。</param>
    /// <param name="chatMessage">聊天消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task InsertRuntimeMessageAsync(
        DbConnection connection,
        DbTransaction transaction,
        long sessionId,
        ChatMessage chatMessage,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO agent_chat_message_runtimes (id, session_id, role, model_content, created_at)
            VALUES ($id, $sessionId, $role, $modelContent, $createdAt);
            """;
        command.AddParameter("$id", SnowflakeIdHelper.Instance.NextId());
        command.AddParameter("$sessionId", sessionId);
        command.AddParameter("$role", chatMessage.Role.Value);
#pragma warning disable MEAI001
        command.AddParameter("$modelContent", AIContentJsonSerializerContext.SerializeContents(chatMessage.Contents));
#pragma warning restore MEAI001
        command.AddParameter("$createdAt", DateTime.Now.FormatDateTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 判断消息是否为框架临时注入（不参与 LLM 恢复也不落 UI 历史）：
    /// AIContextProvider 来源的消息，或缺少真实输入标记的用户消息。
    /// </summary>
    /// <param name="message">聊天消息。</param>
    /// <returns>属于临时消息时返回 <see langword="true" />。</returns>
    private static bool IsTemporaryMessage(ChatMessage message)
    {
        // AIContextProvider 的来源直接为临时消息
        if (message.AdditionalProperties != null
            && message.AdditionalProperties.TryGetValue(
                AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, out var messageSourceAttribution)
            && messageSourceAttribution is AgentRequestMessageSourceAttribution typedAttribution
            && typedAttribution.SourceType == AgentRequestMessageSourceType.AIContextProvider)
        {
            return true;
        }

        // 非用户消息允许参与恢复；用户消息仅真实输入可见，框架补充的上下文视为临时消息
        if (!string.Equals(message.Role.Value, ChatRole.User.Value, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !(TryReadBooleanProperty(message, ChatMessageAdditionalPropertyNames.IsUserInput, out var isUserInput)
                 && isUserInput);
    }

    /// <summary>
    /// 读取布尔类型的聊天消息扩展属性。
    /// </summary>
    /// <param name="message">聊天消息。</param>
    /// <param name="propertyName">属性名。</param>
    /// <param name="value">读取到的值。</param>
    /// <returns>属性存在且可解析为布尔时返回 <see langword="true" />。</returns>
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
    /// 根据 AI 内容集合判断消息类型（用于 agent_chat_messages.type 列）。
    /// </summary>
    /// <param name="contents">AI 内容集合。</param>
    /// <returns>消息类型。</returns>
    private static AgentMessageType GetMessageType(IList<AIContent> contents)
    {
        if (contents is not { Count: > 0 })
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
