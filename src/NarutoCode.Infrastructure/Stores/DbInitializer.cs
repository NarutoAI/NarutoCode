using System.Data.Common;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// 数据库初始化器，当前阶段先保证数据库文件与表结构可自动创建。
/// </summary>
public sealed class DbInitializer(SqliteConnectionFactory connectionFactory)
{
    /// <summary>
    /// 初始化系统所需的本地数据结构。
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        await CreateSchemaAsync(connection, cancellationToken);
        await EnsureConversationTokenCountColumnAsync(connection, cancellationToken);
        await EnsureConversationLastUsageTokenCountColumnAsync(connection, cancellationToken);
        await EnsureMessageVisibilityColumnAsync(connection, cancellationToken);
    }

    /// <summary>
    /// 创建应用运行所需的基础表结构和索引。
    /// </summary>
    /// <param name="connection">数据库连接。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task CreateSchemaAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        // 使用显式 SQL 初始化表结构，避免引入 ORM 运行时模型和 AOT 复杂度。
        var commands = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS "Conversations" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Conversations" PRIMARY KEY AUTOINCREMENT,
                "Title" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                "WorkDirectory" TEXT NOT NULL,
                "TokenCount" INTEGER NOT NULL DEFAULT 0,
                "LastUsageTokenCount" INTEGER NOT NULL DEFAULT 0
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "Messages" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Messages" PRIMARY KEY AUTOINCREMENT,
                "ConversationId" INTEGER NOT NULL,
                "Role" TEXT NOT NULL,
                "Content" TEXT NOT NULL,
                "ModelContent" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "ContentType" TEXT NOT NULL,
                "MessageType" INTEGER NOT NULL,
                "Visibility" TEXT NOT NULL DEFAULT 'Visible',
                CONSTRAINT "FK_Messages_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE
            );
            """,
            "CREATE INDEX IF NOT EXISTS \"IX_Conversations_UpdatedAt\" ON \"Conversations\" (\"UpdatedAt\");",
            "CREATE INDEX IF NOT EXISTS \"IX_Conversations_WorkDirectory\" ON \"Conversations\" (\"WorkDirectory\");",
            "CREATE INDEX IF NOT EXISTS \"IX_Messages_ConversationId\" ON \"Messages\" (\"ConversationId\");",
            "CREATE INDEX IF NOT EXISTS \"IX_Messages_ConversationId_CreatedAt\" ON \"Messages\" (\"ConversationId\", \"CreatedAt\");"
        };

        foreach (var commandText in commands)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 确保旧版本本地数据库包含会话累计 Token 数量字段。
    /// </summary>
    /// <param name="connection">数据库连接。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task EnsureConversationTokenCountColumnAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText = "PRAGMA table_info('Conversations');";
            await using var reader = await checkCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "TokenCount", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = "ALTER TABLE \"Conversations\" ADD COLUMN \"TokenCount\" INTEGER NOT NULL DEFAULT 0;";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 确保旧版本本地数据库包含最近一次对话 Token 消耗字段。
    /// </summary>
    /// <param name="connection">数据库连接。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task EnsureConversationLastUsageTokenCountColumnAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText = "PRAGMA table_info('Conversations');";
            await using var reader = await checkCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "LastUsageTokenCount", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = "ALTER TABLE \"Conversations\" ADD COLUMN \"LastUsageTokenCount\" INTEGER NOT NULL DEFAULT 0;";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 确保旧版本本地数据库包含消息可见性字段。
    /// </summary>
    /// <param name="connection">数据库连接。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task EnsureMessageVisibilityColumnAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText = "PRAGMA table_info('Messages');";
            await using var reader = await checkCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "Visibility", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = "ALTER TABLE \"Messages\" ADD COLUMN \"Visibility\" TEXT NOT NULL DEFAULT 'Visible';";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }
}
