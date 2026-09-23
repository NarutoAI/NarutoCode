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
/// agent_chat_messages（四列 append-only，LLM 恢复 + 审计）与
/// agent_session_items（完成态 UI 渲染历史）在同一事务中写入。
/// </summary>
public class ConversationRepositoryCoordinator(SqliteConnectionFactory connectionFactory)
{
    /// <summary>
    /// 批量追加对话消息与对应的完成态 Item。
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
    /// 在同一个事务中追加消息与完成态 Item、更新 Token 用量并覆盖 LLM 运行时上下文。
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
    /// 写入一条聊天消息（agent_chat_messages 四列），并在同一事务上下文中提取完成态 Item 写入 agent_session_items。
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

        // 1. 写消息行：四列设计（role/type/model_content/created_at）
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

        // 2. 提取完成态 Item（temporary 不落 items，保持 UI 历史纯净）
        if (isTemporary)
        {
            return;
        }

        foreach (var content in chatMessage.Contents)
        {
            var item = TryCreateItem(chatMessage, messageType, content, createdAt);
            if (item is null)
            {
                continue;
            }

            await InsertItemAsync(connection, transaction, conversationId, item.Value, cancellationToken);
        }
    }

    /// <summary>
    /// 从 AI 内容单元提取一条完成态 Item；无法映射的内容返回 <see langword="null" />。
    /// ask_user 交互调用与结果由用户交互 Item（状态机）承载，此处跳过避免双写。
    /// </summary>
    /// <param name="chatMessage">所属聊天消息。</param>
    /// <param name="messageType">消息类型。</param>
    /// <param name="content">AI 内容单元。</param>
    /// <param name="createdAt">落库时间。</param>
    /// <returns>可落库的 Item 元组；不需要落库时为 <see langword="null" />。</returns>
    private static (string Kind, string Status, ConversationItemPayload Payload)? TryCreateItem(
        ChatMessage chatMessage,
        AgentMessageType messageType,
        AIContent content,
        DateTime createdAt)
    {
        var role = chatMessage.Role.Value;
        string kind;
        var status = ConversationItemStatuses.Succeeded;
        string itemContent;
        var toolApprovalContent = string.Empty;

        switch (content)
        {
            case TextContent textContent:
                // 用户真实输入与助手最终文本分别落 userMessage/agentMessage
                kind = string.Equals(role, ChatRole.User.Value, StringComparison.OrdinalIgnoreCase)
                    ? ConversationItemKinds.UserMessage
                    : ConversationItemKinds.AgentMessage;
                itemContent = textContent.Text;
                break;
            case TextReasoningContent textReasoningContent:
                kind = ConversationItemKinds.Reasoning;
                itemContent = textReasoningContent.Text;
                break;
            case FunctionCallContent functionCallContent:
                if (IsUserInteractionFunction(functionCallContent.Name))
                {
                    // 交互问答由 userInteraction Item 承载（等待态 + 终态回写），跳过工具形态双写
                    return null;
                }

                kind = ConversationItemKinds.ToolCall;
                itemContent = functionCallContent.Name;
                break;
            case FunctionResultContent:
                // 工具运行时结果不进 UI 历史（与既有 UI 过滤行为一致）
                return null;
            case ToolApprovalRequestContent
            {
                ToolCall: FunctionCallContent approvalCall
            } approvalRequest:
                // 审批请求卡片：payload 携带完整审批上下文，读取时按位置决定渲染形态
                kind = ConversationItemKinds.ToolApprovalRequest;
                itemContent = approvalCall.Name;
#pragma warning disable MEAI001
                toolApprovalContent =
                    AIContentJsonSerializerContext.SerializeToolApprovalRequestContent(approvalRequest);
#pragma warning restore MEAI001
                break;
            case ToolApprovalResponseContent:
                // 审批响应不进 UI 历史（与既有 UI 过滤行为一致）
                return null;
            case ErrorContent errorContent:
                kind = ConversationItemKinds.Error;
                status = ConversationItemStatuses.Failed;
                itemContent = errorContent.Message;
                break;
            default:
                return null;
        }

        return (kind, status, new ConversationItemPayload(
            role,
            AgentMessageTypeNames.ToName(messageType),
            itemContent,
            toolApprovalContent));
    }

    /// <summary>
    /// 写入一条完成态 Item（agent_session_items）。
    /// </summary>
    private static async Task InsertItemAsync(
        DbConnection connection,
        DbTransaction? transaction,
        long conversationId,
        (string Kind, string Status, ConversationItemPayload Payload) item,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO agent_session_items (id, session_id, kind, status, created_at, payload)
            VALUES ($id, $sessionId, $kind, $status, $createdAt, $payload);
            """;
        AddParameter(command, "$id", SnowflakeIdHelper.Instance.NextId());
        AddParameter(command, "$sessionId", conversationId);
        AddParameter(command, "$kind", item.Kind);
        AddParameter(command, "$status", item.Status);
        AddParameter(command, "$createdAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        AddParameter(command, "$payload", ConversationItemJsonSerializerContext.SerializePayload(item.Payload));
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    /// <summary>
    /// 判断函数调用是否为用户交互工具（ask_user 系）；
    /// 交互问答的 UI 形态由 userInteraction Item 承载，工具调用与结果均不落 Item。
    /// </summary>
    internal static bool IsUserInteractionFunction(string functionName)
    {
        return functionName is "narutocode_ask_user_question"
            // 兼容此前已持久化的旧工具调用，避免历史重载泄露工具名
            or "ask_user_question" or "ask_user_input" or "narutocode_ask_user_input";
    }
}
