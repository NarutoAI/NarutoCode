using System.Data.Common;
using System.Globalization;
using NarutoCode.Domain.Interactions;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// 基于 SQLite 的用户交互仓储实现：负责 AgentInteractions 表的落库、终态回写与启动清理。
/// </summary>
public sealed class UserInteractionRepository(SqliteConnectionFactory connectionFactory) : IUserInteractionStore
{
    /// <inheritdoc />
    public async Task SaveAsync(UserInteractionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Id 为应用侧生成的雪花 ID，直接作为主键写入（非自增）
        command.CommandText =
            """
            INSERT INTO "AgentInteractions" ("Id", "SessionId", "Type", "Title", "Payload", "Status", "Result", "CreatedAt", "CompletedAt")
            VALUES ($id, $sessionId, $type, $title, $payload, $pending, '', $createdAt, NULL);
            """;
        AddParameter(command, "$id", request.Id);
        AddParameter(command, "$sessionId", request.SessionId);
        AddParameter(command, "$type", (int)request.Type);
        AddParameter(command, "$title", request.Title);
        AddParameter(command, "$payload", UserInteractionJsonSerializerContext.SerializeRequest(request));
        AddParameter(command, "$pending", (int)UserInteractionStatus.Pending);
        AddParameter(command, "$createdAt", FormatDateTime(DateTime.Now));
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
            SELECT "Payload"
            FROM "AgentInteractions"
            WHERE "SessionId" = $sessionId AND "Status" = $pending
            ORDER BY "Id";
            """;
        AddParameter(command, "$sessionId", sessionId);
        AddParameter(command, "$pending", (int)UserInteractionStatus.Pending);

        var requests = new List<UserInteractionRequest>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // Payload 反序列化失败时跳过该行，避免单条脏数据阻断清理流程
            var request = UserInteractionJsonSerializerContext.DeserializeRequest(reader.GetString(0));
            if (request is not null)
            {
                requests.Add(request);
            }
        }

        return requests;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(UserInteractionResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // 仅 Pending 可流转到终态：幂等保护，避免取消落库后又被迟到的完成覆盖
        command.CommandText =
            """
            UPDATE "AgentInteractions"
            SET "Status" = $status, "Result" = $result, "CompletedAt" = $completedAt
            WHERE "Id" = $id AND "Status" = $pending;
            """;
        AddParameter(command, "$id", result.InteractionId);
        AddParameter(command, "$status", (int)result.Status);
        AddParameter(command, "$result", UserInteractionJsonSerializerContext.SerializeResult(result));
        AddParameter(command, "$completedAt", FormatDateTime(DateTime.Now));
        AddParameter(command, "$pending", (int)UserInteractionStatus.Pending);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CancelPendingAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // 启动清理：本会话遗留 Pending 全部标记取消（当前无 Run 级恢复能力，重启即作废）
        command.CommandText =
            """
            UPDATE "AgentInteractions"
            SET "Status" = $cancelled, "CompletedAt" = $completedAt
            WHERE "SessionId" = $sessionId AND "Status" = $pending;
            """;
        AddParameter(command, "$cancelled", (int)UserInteractionStatus.Cancelled);
        AddParameter(command, "$completedAt", FormatDateTime(DateTime.Now));
        AddParameter(command, "$sessionId", sessionId);
        AddParameter(command, "$pending", (int)UserInteractionStatus.Pending);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 添加 SQL 参数（跟随 ConversationRepository 的 ADO.NET 显式参数风格）。
    /// </summary>
    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string FormatDateTime(DateTime value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }
}
