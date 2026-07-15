using NarutoCode.Infrastructure.Stores;

namespace NarutoCode.Infrastructure.Tests.Stores;

/// <summary>
/// 验证会话仓储能够按工作目录聚合桌面端工作区摘要。
/// </summary>
[TestClass]
public sealed class ConversationWorkspaceTests
{
    private string? databasePath;

    /// <summary>
    /// 清理测试创建的临时数据库。
    /// </summary>
    [TestCleanup]
    public void Cleanup()
    {
        if (databasePath is not null && File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }

    /// <summary>
    /// 多个会话应按工作目录聚合，并按最近更新时间倒序返回。
    /// </summary>
    [TestMethod]
    public async Task ListWorkspacesAsync_GroupsConversationsByWorkDirectory()
    {
        // Arrange
        databasePath = Path.Combine(Path.GetTempPath(), $"narutocode-workspaces-{Guid.NewGuid():N}.db");
        var connectionFactory = new SqliteConnectionFactory(databasePath);
        await new DbInitializer(connectionFactory).InitializeAsync();
        var repository = new ConversationRepository(connectionFactory);
        var firstPath = Path.Combine(Path.GetTempPath(), "project-a");
        var secondPath = Path.Combine(Path.GetTempPath(), "project-b");

        await repository.CreateForWorkDirectoryAsync(firstPath);
        await repository.CreateForWorkDirectoryAsync(firstPath);
        await repository.CreateForWorkDirectoryAsync(secondPath);

        await using (var connection = await connectionFactory.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "UPDATE \"Conversations\" SET \"UpdatedAt\" = $updatedAt WHERE \"WorkDirectory\" = $path;";
            command.Parameters.AddWithValue("$updatedAt", "2100-01-01T00:00:00.0000000Z");
            command.Parameters.AddWithValue("$path", secondPath);
            await command.ExecuteNonQueryAsync();
        }

        // Act
        var result = await repository.ListWorkspacesAsync();

        // Assert
        Assert.HasCount(2, result);
        Assert.AreEqual(secondPath, result[0].WorkDirectory);
        Assert.AreEqual(1, result[0].ConversationCount);
        Assert.AreEqual(firstPath, result[1].WorkDirectory);
        Assert.AreEqual(2, result[1].ConversationCount);
    }
}
