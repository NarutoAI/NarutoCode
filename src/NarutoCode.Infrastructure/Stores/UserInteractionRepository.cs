using System.Data.Common;
using System.Globalization;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Interactions;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// 基于 agent_session_items 的用户交互仓储实现：
/// 等待态（pending）即落库，作为进程重启后恢复交互卡片的状态源，
/// 终态经回写更新同一行（等待态即落库以支持进程重启后的交互恢复）。
/// </summary>
/// <param name="connectionFactory">SQLite 连接工厂。</param>
public sealed class UserInteractionRepository(SqliteConnectionFactory connectionFactory) : IUserInteractionStore
{
    /// <inheritdoc />
    public async Task SaveAsync(UserInteractionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Id 为应用侧生成的雪花 ID，直接作为 Item 主键写入（非自增）
        command.CommandText =
            """
            INSERT INTO agent_session_items (id, session_id, kind, status, created_at, payload)
            VALUES ($id, $sessionId, $kind, $pending, $createdAt, $payload);
            """;
        AddParameter(command, "$id", request.Id);
        AddParameter(command, "$sessionId", request.SessionId);
        AddParameter(command, "$kind", ConversationItemKinds.UserInteraction);
        AddParameter(command, "$pending", ConversationItemStatuses.Pending);
        AddParameter(command, "$createdAt", request.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        AddParameter(command, "$payload", UserInteractionJsonSerializerContext.SerializeItemPayload(
            new UserInteractionItemPayload(request, null)));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserInteractionRequest>> GetPendingAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT payload
            FROM agent_session_items
            WHERE session_id = $sessionId AND kind = $kind AND status = $pending
            ORDER BY id;
            """;
        AddParameter(command, "$sessionId", sessionId);
        AddParameter(command, "$kind", ConversationItemKinds.UserInteraction);
        AddParameter(command, "$pending", ConversationItemStatuses.Pending);

        var requests = new List<UserInteractionRequest>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // 载荷反序列化失败时跳过该行，避免单条脏数据阻断清理流程
            var payload = UserInteractionJsonSerializerContext.DeserializeItemPayload(reader.GetString(0));
            if (payload?.Request is not null)
            {
                requests.Add(payload.Request);
            }
        }

        return requests;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(UserInteractionResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        // 终态回写需保留请求信息：先读取等待态载荷，合并结果后整行覆盖
        string? payload;
        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText =
                """
                SELECT payload
                FROM agent_session_items
                WHERE id = $id AND kind = $kind AND status = $pending
                LIMIT 1;
                """;
            AddParameter(selectCommand, "$id", result.InteractionId);
            AddParameter(selectCommand, "$kind", ConversationItemKinds.UserInteraction);
            AddParameter(selectCommand, "$pending", ConversationItemStatuses.Pending);
            var scalar = await selectCommand.ExecuteScalarAsync(cancellationToken);
            payload = scalar as string;
        }

        // 等待态行不存在（已终态/已清理）时保持幂等：不覆盖终态
        var existing = UserInteractionJsonSerializerContext.DeserializeItemPayload(payload ?? string.Empty);
        if (existing is null)
        {
            return;
        }

        await using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText =
            """
            UPDATE agent_session_items
            SET status = $status, payload = $payload
            WHERE id = $id AND status = $pending;
            """;
        AddParameter(updateCommand, "$id", result.InteractionId);
        AddParameter(updateCommand, "$status", ToItemStatus(result.Status));
        AddParameter(updateCommand, "$payload", UserInteractionJsonSerializerContext.SerializeItemPayload(
            new UserInteractionItemPayload(existing.Request, result)));
        AddParameter(updateCommand, "$pending", ConversationItemStatuses.Pending);
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CancelPendingAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // 启动清理：本会话遗留 pending 全部标记取消（payload 保留请求信息，UI 按 status 渲染已取消态）
        command.CommandText =
            """
            UPDATE agent_session_items
            SET status = $cancelled
            WHERE session_id = $sessionId AND kind = $kind AND status = $pending;
            """;
        AddParameter(command, "$sessionId", sessionId);
        AddParameter(command, "$kind", ConversationItemKinds.UserInteraction);
        AddParameter(command, "$cancelled", ConversationItemStatuses.Cancelled);
        AddParameter(command, "$pending", ConversationItemStatuses.Pending);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 将交互状态枚举映射为 Item 状态文本。
    /// </summary>
    /// <param name="status">交互状态。</param>
    /// <returns>Item 状态常量。</returns>
    private static string ToItemStatus(UserInteractionStatus status) => status switch
    {
        UserInteractionStatus.Pending => ConversationItemStatuses.Pending,
        UserInteractionStatus.Completed => ConversationItemStatuses.Succeeded,
        UserInteractionStatus.Cancelled => ConversationItemStatuses.Cancelled,
        UserInteractionStatus.Expired => ConversationItemStatuses.Expired,
        _ => ConversationItemStatuses.Cancelled
    };

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
