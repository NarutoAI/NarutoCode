using System.Data.Common;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// agent_workspaces（工作区 / 项目）写入：
/// 幂等创建工作区记录、同步工作区最近更新时间。读取路径见 <see cref="AgentWorkspaceReader" />。
/// </summary>
/// <param name="connectionFactory">SQLite 连接工厂。</param>
public sealed class AgentWorkspaceWriter(SqliteConnectionFactory connectionFactory)
{
    /// <summary>
    /// 幂等确保工作区存在（已存在时保留用户维护的名称与排序值），返回其主键。
    /// </summary>
    /// <param name="workDirectory">规范化后的工作目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作区主键。</returns>
    public async Task<long> EnsureAsync(string workDirectory, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await EnsureAsync(connection, workDirectory, DateTime.Now, cancellationToken);
    }

    /// <summary>
    /// 将工作区更新时间同步为指定时间（不修改名称与排序值）。
    /// </summary>
    /// <param name="workspaceId">工作区主键。</param>
    /// <param name="updatedAt">新的更新时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task TouchAsync(
        long workspaceId,
        DateTime updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await TouchAsync(connection, workspaceId, updatedAt, cancellationToken);
    }

    /// <summary>
    /// 在调用方连接上幂等确保工作区存在（供会话创建等需要单连接完成的编排复用）。
    /// </summary>
    /// <param name="connection">已打开的数据库连接。</param>
    /// <param name="workDirectory">规范化后的工作目录。</param>
    /// <param name="now">创建时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作区主键。</returns>
    internal static async Task<long> EnsureAsync(
        DbConnection connection,
        string workDirectory,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            // ON CONFLICT DO NOTHING：已存在的工作区保留现有名称与排序值
            command.CommandText =
                """
                INSERT INTO agent_workspaces (name, work_directory, sort_order, created_at, updated_at)
                VALUES ($name, $workDirectory, 0, $createdAt, $updatedAt)
                ON CONFLICT(work_directory) DO NOTHING;
                """;
            command.AddParameter("$name", AgentWorkspaceReader.CreateName(workDirectory));
            command.AddParameter("$workDirectory", workDirectory);
            command.AddParameter("$createdAt", now.FormatDateTime());
            command.AddParameter("$updatedAt", now.FormatDateTime());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var workspaceId = await AgentWorkspaceReader.FindIdAsync(connection, workDirectory, cancellationToken);
        return workspaceId ?? throw new InvalidOperationException($"工作区写入后无法读取：{workDirectory}");
    }

    /// <summary>
    /// 在调用方连接上同步工作区更新时间（供会话创建等需要单连接完成的编排复用）。
    /// </summary>
    /// <param name="connection">已打开的数据库连接。</param>
    /// <param name="workspaceId">工作区主键。</param>
    /// <param name="updatedAt">新的更新时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    internal static async Task TouchAsync(
        DbConnection connection,
        long workspaceId,
        DateTime updatedAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE agent_workspaces SET updated_at = $updatedAt WHERE id = $workspaceId;";
        command.AddParameter("$updatedAt", updatedAt.FormatDateTime());
        command.AddParameter("$workspaceId", workspaceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
