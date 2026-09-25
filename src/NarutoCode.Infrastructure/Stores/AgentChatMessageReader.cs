using NarutoCode.Domain.Entities;
using NarutoCode.Domain.Messages;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// agent_chat_messages 与 agent_chat_message_runtimes（面向模型的聊天历史）只读访问：
/// 全量历史用于 LLM 恢复与审计，运行时上下文为压缩策略裁剪后的发送视图。
/// 写入路径见 <see cref="AgentChatMessageWriter" />。
/// </summary>
/// <param name="connectionFactory">SQLite 连接工厂。</param>
public sealed class AgentChatMessageReader(SqliteConnectionFactory connectionFactory)
{
    /// <summary>
    /// 按会话读取全量聊天历史（排除 <c>temporary</c> 框架临时注入，按写入时间与主键排序）。
    /// </summary>
    /// <param name="sessionId">会话主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>聊天历史消息集合。</returns>
    public async Task<IReadOnlyList<Message>> ListMessagesAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, session_id, role, type, model_content, created_at
            FROM agent_chat_messages
            WHERE session_id = $sessionId
              AND type <> $temporary
            ORDER BY created_at, id;
            """;
        command.AddParameter("$sessionId", sessionId);
        command.AddParameter("$temporary", AgentMessageTypeNames.Temporary);

        var messages = new List<Message>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new Message
            {
                Id = reader.GetInt64(0),
                ConversationId = reader.GetInt64(1),
                Role = reader.GetString(2),
                Type = reader.GetString(3),
                ModelContent = reader.GetString(4),
                CreatedAt = reader.ReadDateTime(5)
            });
        }

        return messages;
    }

    /// <summary>
    /// 按会话读取发送给 LLM 的运行时上下文（覆盖式表，按雪花主键单调排序）。
    /// </summary>
    /// <param name="sessionId">会话主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>运行时上下文消息集合。</returns>
    public async Task<IReadOnlyList<Message>> ListRuntimeMessagesAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, session_id, role, model_content, created_at
            FROM agent_chat_message_runtimes
            WHERE session_id = $sessionId
            ORDER BY id;
            """;
        command.AddParameter("$sessionId", sessionId);

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
                CreatedAt = reader.ReadDateTime(4),
                // 运行时上下文不分类型（无 type 列），统一视为普通内容消息
                Type = AgentMessageTypeNames.Content
            });
        }

        return messages;
    }
}
