using System.Data.Common;
using System.Globalization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NarutoCode.Domain;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Messages;
using NarutoCode.Infrastructure.AIAgents;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// 负责将 Agent 消息批量写入本地会话数据库：
/// agent_chat_messages（四列 append-only，LLM 恢复 + 审计，面向模型）
/// 与 agent_chat_message_runtimes（覆盖式运行时上下文）。
/// UI 渲染历史（agent_session_items，面向 UI）由 MafAgentItemTracker 独立写入，二者职责分离。
/// </summary>
public class ConversationRepositoryCoordinator(SqliteConnectionFactory connectionFactory)
{
    /// <summary>
    /// 批量追加对话消息（不产生 UI Item）。
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

        foreach (var message in messages)
        {
            await InsertMessageAsync(
                connection,
                transaction: null,
                conversationId,
                message,
                cancellationToken);
        }

        // 维护会话的 Token 使用量
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
    /// 在同一个事务中追加消息、更新 Token 用量并覆盖 LLM 运行时上下文。
    /// </summary>
    /// <param name="conversationId">对话 ID。</param>
    /// <param name="messages">待追加到历史的新增消息。</param>
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

        foreach (var runtimeMessage in runtimeMessages)
        {
            await InsertRuntimeMessageAsync(
                connection,
                transaction,
                conversationId,
                runtimeMessage,
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

        foreach (var message in messages)
        {
            await InsertRuntimeMessageAsync(
                connection,
                transaction,
                conversationId,
                message,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 写入一条聊天消息（agent_chat_messages 四列，append-only）。
    /// </summary>
    /// <param name="connection">数据库连接。</param>
    /// <param name="transaction">当前事务；无事务时为 <see langword="null" />。</param>
    /// <param name="conversationId">对话 ID。</param>
    /// <param name="chatMessage">聊天消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task InsertMessageAsync(
        DbConnection connection,
        DbTransaction? transaction,
        long conversationId,
        ChatMessage chatMessage,
        CancellationToken cancellationToken)
    {
        // 类型判定：AIContextProvider 注入或非真实用户输入 → temporary（不进 UI 历史也不参与恢复）
        var isTemporary = IsTemporaryMessage(chatMessage);
        var messageType = isTemporary
            ? AgentMessageType.Temporary
            : GetMessageType(chatMessage.Contents);
        var createdAt = DateTime.Now;
        var messageId = SnowflakeIdHelper.Instance.NextId();

        // 四列设计（role/type/model_content/created_at），面向模型的完整持久化
        await using (DbCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO agent_chat_messages (id, session_id, role, type, model_content, created_at)
                VALUES ($id, $sessionId, $role, $type, $modelContent, $createdAt);
                """;
            AddParameter(command, "$id", messageId);
            AddParameter(command, "$sessionId", conversationId);
            AddParameter(command, "$role", chatMessage.Role.Value);
#pragma warning disable MEAI001
            AddParameter(command, "$type", AgentMessageTypeNames.ToName(messageType));
            AddParameter(command, "$modelContent", AIContentJsonSerializerContext.SerializeContents(chatMessage.Contents));
#pragma warning restore MEAI001
            AddParameter(command, "$createdAt", createdAt.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 删除指定对话已有的 LLM 运行时上下文消息。
    /// </summary>
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
            DELETE FROM agent_chat_message_runtimes
            WHERE session_id = $sessionId;
            """;
        AddParameter(deleteCommand, "$sessionId", conversationId);
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 插入一条发送给 LLM 的运行时上下文消息（覆盖式表，按雪花 id 排序）。
    /// </summary>
    private static async Task InsertRuntimeMessageAsync(
        DbConnection connection,
        DbTransaction transaction,
        long conversationId,
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
        AddParameter(command, "$id", SnowflakeIdHelper.Instance.NextId());
        AddParameter(command, "$sessionId", conversationId);
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
            UPDATE agent_sessions
            SET token_count = token_count + $tokenCount,
                last_usage_token_count = $tokenCount,
                last_input_token_count = $inputTokenCount
            WHERE id = $sessionId;
            """;
        AddParameter(command, "$sessionId", conversationId);
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
    /// 判断消息是否为框架临时注入（不进 UI 历史也不参与恢复）：
    /// AIContextProvider 来源的消息，或缺少真实输入标记的用户消息。
    /// </summary>
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

        // 非用户消息允许显示；用户消息仅真实输入可见，框架补充的上下文视为临时消息
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
