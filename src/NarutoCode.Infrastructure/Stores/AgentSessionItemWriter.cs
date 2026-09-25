using System.Data.Common;
using NarutoCode.Domain;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// agent_session_items（UI 渲染历史）写入：完成态 Item 由 <c>MafAgentItemTracker</c> 在流式输出侧构造后经此落库。
/// 面向 UI 的通道，与面向模型的 <see cref="AgentChatMessageWriter" /> 完全独立；读取路径见 <see cref="AgentSessionItemReader" />。
/// </summary>
/// <param name="connectionFactory">SQLite 连接工厂。</param>
public sealed class AgentSessionItemWriter(SqliteConnectionFactory connectionFactory)
{
    /// <summary>
    /// 写入一条 Items 记录（主键在写入时生成雪花 ID）。
    /// </summary>
    /// <param name="sessionId">会话主键。</param>
    /// <param name="kind">Item 类型，取值见 <see cref="NarutoCode.Domain.Conversations.ConversationItemKinds" />。</param>
    /// <param name="status">Item 状态，取值见 <see cref="NarutoCode.Domain.Conversations.ConversationItemStatuses" />。</param>
    /// <param name="payload">强类型载荷，按 kind 对应特定 record 序列化。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task InsertAsync(
        long sessionId,
        string kind,
        string status,
        object payload,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await InsertAsync(connection, transaction: null, sessionId, kind, status, payload, cancellationToken);
    }

    /// <summary>
    /// 在既有连接 / 事务上写入一条 Items 记录（供需要事务包裹的调用方使用）。
    /// </summary>
    /// <param name="connection">已打开的数据库连接。</param>
    /// <param name="transaction">当前事务；无事务时为 <see langword="null" />。</param>
    /// <param name="sessionId">会话主键。</param>
    /// <param name="kind">Item 类型。</param>
    /// <param name="status">Item 状态。</param>
    /// <param name="payload">强类型载荷。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task InsertAsync(
        DbConnection connection,
        DbTransaction? transaction,
        long sessionId,
        string kind,
        string status,
        object payload,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO agent_session_items (id, session_id, kind, status, created_at, payload)
            VALUES ($id, $sessionId, $kind, $status, $createdAt, $payload);
            """;
        command.AddParameter("$id", SnowflakeIdHelper.Instance.NextId());
        command.AddParameter("$sessionId", sessionId);
        command.AddParameter("$kind", kind);
        command.AddParameter("$status", status);
        command.AddParameter("$createdAt", DateTime.Now.FormatDateTime());
        command.AddParameter("$payload", ConversationItemJsonSerializerContext.SerializePayload(payload));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
