using NarutoCode.Infrastructure.Stores;

namespace NarutoCode.Infrastructure.Tests.Stores;

/// <summary>
/// 验证项目表通过项目主键关联会话并提供桌面端项目摘要。
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
    /// 项目应通过项目主键关联会话，并按关联会话最近更新时间排序。
    /// </summary>
    [TestMethod]
    public async Task ListWorkspacesAsync_JoinsProjectsAndConversationsByProjectId()
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
        Assert.AreNotEqual(0L, result[0].Id);
        Assert.AreEqual("project-b", result[0].Name);
        Assert.AreEqual(secondPath, result[0].WorkDirectory);
        Assert.AreEqual(1, result[0].ConversationCount);
        Assert.AreNotEqual(0L, result[1].Id);
        Assert.AreEqual("project-a", result[1].Name);
        Assert.AreEqual(firstPath, result[1].WorkDirectory);
        Assert.AreEqual(2, result[1].ConversationCount);

        var projectBConversations = await repository.ListByProjectIdAsync(result[0].Id);
        Assert.HasCount(1, projectBConversations);

        await using var projectConnection = await connectionFactory.OpenConnectionAsync();
        await using var projectCommand = projectConnection.CreateCommand();
        projectCommand.CommandText = "SELECT COUNT(*) FROM \"Conversations\" WHERE \"ProjectId\" = $projectId;";
        projectCommand.Parameters.AddWithValue("$projectId", result[1].Id);
        Assert.AreEqual(2L, Convert.ToInt64(await projectCommand.ExecuteScalarAsync()));
    }
}
