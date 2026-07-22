using System.Data.Common;
using NarutoCode.Domain.Workspaces;

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
        await EnsureConversationLastInputTokenCountColumnAsync(connection, cancellationToken);
        await EnsureConversationProjectIdColumnAsync(connection, cancellationToken);
        await EnsureConversationSourceColumnAsync(connection, cancellationToken);
        await EnsureMessageVisibilityColumnAsync(connection, cancellationToken);
        await NormalizeConversationWorkDirectoriesAsync(connection, cancellationToken);
        await BackfillProjectsFromConversationsAsync(connection, cancellationToken);
        await BackfillConversationProjectIdsAsync(connection, cancellationToken);
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
            CREATE TABLE IF NOT EXISTS "Projects" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Projects" PRIMARY KEY AUTOINCREMENT,
                "Name" TEXT NOT NULL,
                "WorkDirectory" TEXT NOT NULL,
                "SortOrder" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "Conversations" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Conversations" PRIMARY KEY AUTOINCREMENT,
                "Title" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                "ProjectId" INTEGER NOT NULL,
                "WorkDirectory" TEXT NOT NULL,
                "TokenCount" INTEGER NOT NULL DEFAULT 0,
                "LastUsageTokenCount" INTEGER NOT NULL DEFAULT 0,
                "LastInputTokenCount" INTEGER NOT NULL DEFAULT 0,
                "Source" INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT "FK_Conversations_Projects_ProjectId" FOREIGN KEY ("ProjectId") REFERENCES "Projects" ("Id") ON DELETE RESTRICT
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
            """
            CREATE TABLE IF NOT EXISTS "ConversationRuntimeMessages" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ConversationRuntimeMessages" PRIMARY KEY AUTOINCREMENT,
                "ConversationId" INTEGER NOT NULL,
                "Sequence" INTEGER NOT NULL,
                "Role" TEXT NOT NULL,
                "ModelContent" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_ConversationRuntimeMessages_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE
            );
            """,
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Projects_WorkDirectory\" ON \"Projects\" (\"WorkDirectory\");",
            "CREATE INDEX IF NOT EXISTS \"IX_Projects_SortOrder_UpdatedAt\" ON \"Projects\" (\"SortOrder\", \"UpdatedAt\" DESC);",
            "CREATE INDEX IF NOT EXISTS \"IX_Conversations_UpdatedAt\" ON \"Conversations\" (\"UpdatedAt\");",
            "CREATE INDEX IF NOT EXISTS \"IX_Conversations_WorkDirectory\" ON \"Conversations\" (\"WorkDirectory\");",
            "CREATE INDEX IF NOT EXISTS \"IX_Conversations_ProjectId_UpdatedAt\" ON \"Conversations\" (\"ProjectId\", \"UpdatedAt\" DESC);",
            "CREATE INDEX IF NOT EXISTS \"IX_Conversations_ProjectId_Source_UpdatedAt\" ON \"Conversations\" (\"ProjectId\", \"Source\", \"UpdatedAt\" DESC);",
            "CREATE INDEX IF NOT EXISTS \"IX_Messages_ConversationId\" ON \"Messages\" (\"ConversationId\");",
            "CREATE INDEX IF NOT EXISTS \"IX_Messages_ConversationId_CreatedAt\" ON \"Messages\" (\"ConversationId\", \"CreatedAt\");",
            "CREATE INDEX IF NOT EXISTS \"IX_ConversationRuntimeMessages_ConversationId\" ON \"ConversationRuntimeMessages\" (\"ConversationId\");",
            "CREATE INDEX IF NOT EXISTS \"IX_ConversationRuntimeMessages_ConversationId_Sequence\" ON \"ConversationRuntimeMessages\" (\"ConversationId\", \"Sequence\");"
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
    /// 确保旧版本本地数据库包含最近一次输入 Token 数量字段。
    /// </summary>
    /// <param name="connection">数据库连接。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task EnsureConversationLastInputTokenCountColumnAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText = "PRAGMA table_info('Conversations');";
            await using var reader = await checkCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "LastInputTokenCount", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = "ALTER TABLE \"Conversations\" ADD COLUMN \"LastInputTokenCount\" INTEGER NOT NULL DEFAULT 0;";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 确保旧版本本地数据库包含会话所属项目字段。
    /// </summary>
    /// <param name="connection">数据库连接。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task EnsureConversationProjectIdColumnAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText = "PRAGMA table_info('Conversations');";
            await using var reader = await checkCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "ProjectId", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        // 旧表通过可空列平滑升级；完成 Projects 回填后统一填充该字段。
        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = "ALTER TABLE \"Conversations\" ADD COLUMN \"ProjectId\" INTEGER NULL;";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 确保旧版本本地数据库包含会话来源字段。
    /// </summary>
    private static async Task EnsureConversationSourceColumnAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText = "PRAGMA table_info('Conversations');";
            await using var reader = await checkCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "Source", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        // 默认值 0 = Local，存量会话全部标记为本地来源
        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = "ALTER TABLE \"Conversations\" ADD COLUMN \"Source\" INTEGER NOT NULL DEFAULT 0;";
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

    /// <summary>
    /// 规范化历史会话工作目录，确保其可与 Projects.WorkDirectory 进行稳定关联。
    /// </summary>
    private static async Task NormalizeConversationWorkDirectoriesAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var directories = new List<string>();
        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText = "SELECT DISTINCT \"WorkDirectory\" FROM \"Conversations\" WHERE \"WorkDirectory\" <> '';";
            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                directories.Add(reader.GetString(0));
            }
        }

        foreach (var directory in directories)
        {
            var normalizedDirectory = WorkspacePath.Normalize(directory);
            if (string.Equals(directory, normalizedDirectory, StringComparison.Ordinal))
            {
                continue;
            }

            await using var updateCommand = connection.CreateCommand();
            updateCommand.CommandText =
                "UPDATE \"Conversations\" SET \"WorkDirectory\" = $normalizedDirectory WHERE \"WorkDirectory\" = $workDirectory;";
            AddParameter(updateCommand, "$normalizedDirectory", normalizedDirectory);
            AddParameter(updateCommand, "$workDirectory", directory);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 根据历史会话补齐项目记录，保证升级前已有目录也能显示在项目列表中。
    /// </summary>
    private static async Task BackfillProjectsFromConversationsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var projects = new List<(string WorkDirectory, DateTime CreatedAt, DateTime UpdatedAt)>();
        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText =
                "SELECT \"WorkDirectory\", MIN(\"CreatedAt\"), MAX(\"UpdatedAt\") FROM \"Conversations\" WHERE \"WorkDirectory\" <> '' GROUP BY \"WorkDirectory\";";
            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                projects.Add((reader.GetString(0), ReadDateTime(reader, 1), ReadDateTime(reader, 2)));
            }
        }

        foreach (var project in projects)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText =
                """
                INSERT INTO "Projects" ("Name", "WorkDirectory", "SortOrder", "CreatedAt", "UpdatedAt")
                VALUES ($name, $workDirectory, 0, $createdAt, $updatedAt)
                ON CONFLICT("WorkDirectory") DO NOTHING;
                """;
            AddParameter(insertCommand, "$name", CreateProjectName(project.WorkDirectory));
            AddParameter(insertCommand, "$workDirectory", project.WorkDirectory);
            AddParameter(insertCommand, "$createdAt", FormatDateTime(project.CreatedAt));
            AddParameter(insertCommand, "$updatedAt", FormatDateTime(project.UpdatedAt));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 将历史会话关联到对应项目，后续查询统一使用项目主键。
    /// </summary>
    private static async Task BackfillConversationProjectIdsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE "Conversations"
            SET "ProjectId" = (
                SELECT p."Id"
                FROM "Projects" p
                WHERE p."WorkDirectory" = "Conversations"."WorkDirectory"
            )
            WHERE "ProjectId" IS NULL
              AND "WorkDirectory" <> '';
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 创建项目的默认显示名称。
    /// </summary>
    private static string CreateProjectName(string workDirectory)
    {
        var name = Path.GetFileName(workDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? workDirectory : name;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static DateTime ReadDateTime(DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value is DateTime dateTime
            ? dateTime
            : DateTime.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatDateTime(DateTime value) =>
        value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
