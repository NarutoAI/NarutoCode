using Microsoft.Data.Sqlite;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// 创建 NarutoCode 本地 SQLite 数据库连接。
/// </summary>
public sealed class SqliteConnectionFactory(string databasePath)
{
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared
    }.ToString();

    /// <summary>
    /// 创建并打开 SQLite 连接。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已打开的 SQLite 连接。</returns>
    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
