using System.Data.Common;
using System.Globalization;
using NarutoCode.Domain;
using NarutoCode.Domain.Conversations;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// agent_session_items（UI 渲染历史）的写入器：
/// 完成态 Item 由 MafAgentItemTracker 在流式输出侧构造后经此落库。
/// 面向 UI 的通道，与面向模型的 ConversationRepositoryCoordinator 完全独立。
/// </summary>
/// <param name="connectionFactory">SQLite 连接工厂。</param>
public class SqliteSessionItemWriter(SqliteConnectionFactory connectionFactory)
{
    /// <summary>
    /// 写入一条完成态 Item。
    /// </summary>
    /// <param name="sessionId">会话 ID。</param>
    /// <param name="kind">Item 类型（ConversationItemKinds 常量）。</param>
    /// <param name="status">Item 状态（ConversationItemStatuses 常量）。</param>
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
        await InsertAsync(connection, null, sessionId, kind, status, payload, cancellationToken);
    }

    /// <summary>
    /// 在既有连接/事务上写入一条完成态 Item（供需要事务包裹的调用方使用）。
    /// </summary>
    /// <param name="connection">数据库连接。</param>
    /// <param name="transaction">当前事务；无事务时为 <see langword="null" />。</param>
    /// <param name="sessionId">会话 ID。</param>
    /// <param name="kind">Item 类型（ConversationItemKinds 常量）。</param>
    /// <param name="status">Item 状态（ConversationItemStatuses 常量）。</param>
    /// <param name="payload">强类型载荷，按 kind 对应特定 record 序列化。</param>
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
        AddParameter(command, "$id", SnowflakeIdHelper.Instance.NextId());
        AddParameter(command, "$sessionId", sessionId);
        AddParameter(command, "$kind", kind);
        AddParameter(command, "$status", status);
        AddParameter(command, "$createdAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        AddParameter(command, "$payload", ConversationItemJsonSerializerContext.SerializePayload(payload));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 添加 SQL 参数（跟随 ConversationRepository 的 ADO.NET 显式参数风格）。
    /// </summary>
    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
