using System.Data.Common;

namespace NarutoCode.Infrastructure.Stores;

/// <summary>
/// 数据库初始化器：创建本地 SQLite 表结构（snake_case 命名、雪花主键、无外键）。
/// 新表使用全新表名，与历史遗留表互不冲突；旧表不删除也不迁移，留存于库文件中。
/// </summary>
public sealed class DbInitializer(SqliteConnectionFactory connectionFactory)
{
    /// <summary>
    /// 初始化系统所需的本地数据结构：按需创建新表与索引（幂等）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        await CreateSchemaAsync(connection, cancellationToken);
    }

    /// <summary>
    /// 创建新表结构与索引。
    /// 约定：
    /// - 主键统一命名 id（雪花 BIGINT，应用层 SnowflakeIdHelper 生成，不使用自增）；
    /// - 不使用外键约束，引用完整性由应用层保证；
    /// - 时间列统一 TEXT，应用层写入本地时间 ISO-8601 文本。
    /// </summary>
    /// <param name="connection">数据库连接。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task CreateSchemaAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        // 使用显式 SQL 初始化表结构，避免引入 ORM 运行时模型和 AOT 复杂度。
        var commands = new[]
        {
            // 1. 工作目录实体表（原 Projects；侧边栏分组维度）
            """
            CREATE TABLE IF NOT EXISTS "agent_workspaces" (
                "id"             INTEGER NOT NULL PRIMARY KEY,
                "name"           TEXT    NOT NULL,
                "work_directory" TEXT    NOT NULL,
                "sort_order"     INTEGER NOT NULL DEFAULT 0,
                "created_at"     TEXT    NOT NULL,
                "updated_at"     TEXT    NOT NULL
            );
            """,
            // 2. 会话记录表（原 Conversations；新增 LLM 元数据三列，source 保持 int 枚举）
            """
            CREATE TABLE IF NOT EXISTS "agent_sessions" (
                "id"                     INTEGER NOT NULL PRIMARY KEY,
                "workspace_id"           INTEGER NOT NULL,
                "work_directory"         TEXT    NOT NULL,
                "title"                  TEXT    NOT NULL,
                "source"                 INTEGER NOT NULL DEFAULT 0,
                "source_id"              TEXT    NOT NULL DEFAULT '',
                "llm_provider"           TEXT    NOT NULL DEFAULT '',
                "llm_model"              TEXT    NOT NULL DEFAULT '',
                "reasoning_effort"       TEXT    NOT NULL DEFAULT '',
                "token_count"            INTEGER NOT NULL DEFAULT 0,
                "last_usage_token_count" INTEGER NOT NULL DEFAULT 0,
                "last_input_token_count" INTEGER NOT NULL DEFAULT 0,
                "created_at"             TEXT    NOT NULL,
                "updated_at"             TEXT    NOT NULL
            );
            """,
            // 3. 历史 Item 表（新增；UI 渲染历史 + 用户交互等待态，仅完成态落库，交互 pending 例外）
            """
            CREATE TABLE IF NOT EXISTS "agent_session_items" (
                "id"         INTEGER NOT NULL PRIMARY KEY,
                "session_id" INTEGER NOT NULL,
                "kind"       TEXT    NOT NULL,
                "status"     TEXT    NOT NULL,
                "created_at" TEXT    NOT NULL,
                "payload"    TEXT    NOT NULL
            );
            """,
            // 4. LLM 聊天消息表（原 Messages；收敛为四列 + id/session_id，append-only）
            """
            CREATE TABLE IF NOT EXISTS "agent_chat_messages" (
                "id"            INTEGER NOT NULL PRIMARY KEY,
                "session_id"    INTEGER NOT NULL,
                "role"          TEXT    NOT NULL,
                "type"          TEXT    NOT NULL,
                "model_content" TEXT    NOT NULL,
                "created_at"    TEXT    NOT NULL
            );
            """,
            // 5. LLM 运行时上下文消息表（原 ConversationRuntimeMessages；覆盖式，去掉 Sequence 列按 id 排序）
            """
            CREATE TABLE IF NOT EXISTS "agent_chat_message_runtimes" (
                "id"            INTEGER NOT NULL PRIMARY KEY,
                "session_id"    INTEGER NOT NULL,
                "role"          TEXT    NOT NULL,
                "model_content" TEXT    NOT NULL,
                "created_at"    TEXT    NOT NULL
            );
            """,
            """CREATE UNIQUE INDEX IF NOT EXISTS "uk_workspaces_work_directory" ON "agent_workspaces" ("work_directory");""",
            """CREATE INDEX IF NOT EXISTS "ix_workspaces_sort_updated" ON "agent_workspaces" ("sort_order", "updated_at" DESC);""",
            """CREATE INDEX IF NOT EXISTS "ix_sessions_workspace_updated" ON "agent_sessions" ("workspace_id", "updated_at" DESC);""",
            """CREATE INDEX IF NOT EXISTS "ix_sessions_workspace_source" ON "agent_sessions" ("workspace_id", "source", "source_id", "updated_at" DESC);""",
            """CREATE INDEX IF NOT EXISTS "ix_sessions_work_directory" ON "agent_sessions" ("work_directory");""",
            """CREATE INDEX IF NOT EXISTS "ix_items_session_id" ON "agent_session_items" ("session_id", "id");""",
            """CREATE INDEX IF NOT EXISTS "ix_items_session_kind_status" ON "agent_session_items" ("session_id", "kind", "status");""",
            """CREATE INDEX IF NOT EXISTS "ix_messages_session_id" ON "agent_chat_messages" ("session_id", "id");""",
            """CREATE INDEX IF NOT EXISTS "ix_runtime_messages_session_id" ON "agent_chat_message_runtimes" ("session_id", "id");"""
        };

        foreach (var commandText in commands)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
