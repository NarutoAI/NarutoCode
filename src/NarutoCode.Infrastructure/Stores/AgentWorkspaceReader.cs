using System.Data.Common;
using System.Globalization;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Enums;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// agent_workspaces（工作区 / 项目）只读访问：
/// 工作区摘要列表、单工作区摘要、工作目录解析与名称生成。
/// 写入路径见 <see cref="AgentWorkspaceWriter" />。
/// </summary>
/// <param name="connectionFactory">SQLite 连接工厂。</param>
public sealed class AgentWorkspaceReader(SqliteConnectionFactory connectionFactory)
{
    /// <summary>
    /// 按排序值与最近更新时间列出包含历史会话的工作区。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作区摘要集合。</returns>
    public async Task<IReadOnlyList<WorkspaceSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // 会话数仅统计本地来源会话（通道会话不在 TUI/桌面端展示）
        command.CommandText =
            """
            SELECT
                w.id,
                w.name,
                w.work_directory,
                w.sort_order,
                w.created_at,
                w.updated_at,
                COALESCE(MAX(c.updated_at), w.updated_at) AS last_updated_at,
                COUNT(c.id) AS conversation_count
            FROM agent_workspaces w
            LEFT JOIN agent_sessions c ON c.workspace_id = w.id AND c.source = $localSource
            GROUP BY w.id, w.name, w.work_directory, w.sort_order, w.created_at, w.updated_at
            ORDER BY w.sort_order, last_updated_at DESC, w.id;
            """;
        command.AddParameter("$localSource", (int)ConversationSource.Local);

        var workspaces = new List<WorkspaceSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            workspaces.Add(ReadSummary(reader));
        }

        return workspaces;
    }

    /// <summary>
    /// 按主键读取单个工作区摘要（含会话数）。
    /// </summary>
    /// <param name="workspaceId">工作区主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作区摘要；不存在时返回 <see langword="null" />。</returns>
    public async Task<WorkspaceSummary?> GetSummaryAsync(
        long workspaceId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                w.id,
                w.name,
                w.work_directory,
                w.sort_order,
                w.created_at,
                w.updated_at,
                COALESCE(MAX(c.updated_at), w.updated_at) AS last_updated_at,
                COUNT(c.id) AS conversation_count
            FROM agent_workspaces w
            LEFT JOIN agent_sessions c ON c.workspace_id = w.id AND c.source = $localSource
            WHERE w.id = $workspaceId
            GROUP BY w.id, w.name, w.work_directory, w.sort_order, w.created_at, w.updated_at;
            """;
        command.AddParameter("$workspaceId", workspaceId);
        command.AddParameter("$localSource", (int)ConversationSource.Local);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSummary(reader) : null;
    }

    /// <summary>
    /// 在工作目录上读取工作区主键（供写入路径在已有连接上定位工作区）。
    /// </summary>
    /// <param name="connection">已打开的数据库连接。</param>
    /// <param name="workDirectory">规范化后的工作目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作区主键；不存在时返回 <see langword="null" />。</returns>
    internal static async Task<long?> FindIdAsync(
        DbConnection connection,
        string workDirectory,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM agent_workspaces WHERE work_directory = $workDirectory LIMIT 1;";
        command.AddParameter("$workDirectory", workDirectory);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 读取工作区绑定的工作目录（会话创建时用于生成标题并落库）。
    /// </summary>
    /// <param name="connection">已打开的数据库连接。</param>
    /// <param name="workspaceId">工作区主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作目录；工作区不存在时返回 <see langword="null" />。</returns>
    internal static async Task<string?> GetWorkDirectoryAsync(
        DbConnection connection,
        long workspaceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT work_directory FROM agent_workspaces WHERE id = $workspaceId LIMIT 1;";
        command.AddParameter("$workspaceId", workspaceId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToString(result, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 从工作目录生成工作区 / 会话默认名称（取路径末段，根目录回退为原路径）。
    /// </summary>
    /// <param name="workDirectory">规范化后的工作目录。</param>
    /// <returns>显示名称。</returns>
    internal static string CreateName(string workDirectory)
    {
        var name = Path.GetFileName(workDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? workDirectory : name;
    }

    /// <summary>
    /// 将查询行读取为工作区摘要（列顺序与上方查询一致）。
    /// </summary>
    /// <param name="reader">数据读取器。</param>
    /// <returns>工作区摘要。</returns>
    private static WorkspaceSummary ReadSummary(DbDataReader reader)
    {
        return new WorkspaceSummary(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.ReadDateTime(4),
            reader.ReadDateTime(5),
            reader.ReadDateTime(6),
            Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture));
    }
}
