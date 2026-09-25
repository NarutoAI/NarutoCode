using NarutoCode.Domain.Conversations;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// agent_session_items（UI 渲染历史）只读访问：按会话读取 Item 并投影为 TUI 历史消息。
/// 写入路径见 <see cref="AgentSessionItemWriter" />；用户交互状态机见 <see cref="AgentSessionItemInteractionStore" />。
/// </summary>
/// <param name="connectionFactory">SQLite 连接工厂。</param>
public sealed class AgentSessionItemReader(SqliteConnectionFactory connectionFactory)
{
    /// <summary>
    /// 按会话读取 UI 渲染历史（按雪花主键单调排序，与写入顺序一致）。
    /// </summary>
    /// <param name="sessionId">会话主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按时间顺序排列的 UI 历史消息。</returns>
    public async Task<IReadOnlyList<ConversationHistoryMessage>> ListHistoryAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT kind, status, payload, created_at
            FROM agent_session_items
            WHERE session_id = $sessionId
            ORDER BY id;
            """;
        command.AddParameter("$sessionId", sessionId);

        var messages = new List<ConversationHistoryMessage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // 投影失败（非法载荷 / 未完成态）返回 null，跳过该行但不阻断整体历史渲染
            var historyMessage = AgentSessionItemProjector.Project(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.ReadDateTime(3));
            if (historyMessage is not null)
            {
                messages.Add(historyMessage);
            }
        }

        return messages;
    }
}
