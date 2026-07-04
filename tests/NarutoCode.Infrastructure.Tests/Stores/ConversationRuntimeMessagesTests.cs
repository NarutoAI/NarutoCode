using Microsoft.Extensions.AI;
using NarutoCode.Infrastructure.AIAgents.ChatHistorys;
using NarutoCode.Infrastructure.Stores;

namespace NarutoCode.Infrastructure.Tests.Stores;

/// <summary>
/// 验证 UI 展示消息与 LLM 运行时上下文消息使用独立存储。
/// </summary>
[TestClass]
public sealed class ConversationRuntimeMessagesTests
{
    private string? databasePath;

    /// <summary>
    /// 清理测试创建的临时数据库文件。
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
    /// Runtime 消息应独立于 UI 消息读取，避免重启后从完整 UI 历史重新裁剪。
    /// </summary>
    [TestMethod]
    public async Task ChatHistoryPersistenceHandler_WhenPersistingMessages_WritesUiAndRuntimeHistoriesSeparately()
    {
        // Arrange
        databasePath = Path.Combine(Path.GetTempPath(), $"narutocode-runtime-{Guid.NewGuid():N}.db");
        var connectionFactory = new SqliteConnectionFactory(databasePath);
        var initializer = new DbInitializer(connectionFactory);
        await initializer.InitializeAsync();

        var repository = new ConversationRepository(connectionFactory);
        var coordinator = new ConversationRepositoryCoordinator(connectionFactory);
        var handler = new ConversationChatHistoryPersistenceHandler(coordinator);
        var conversation = await repository.CreateForWorkDirectoryAsync(Path.GetTempPath());

        var uiMessage = new ChatMessage(ChatRole.User, "full-ui-handler-message")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["NarutoCode.IsUserInput"] = true
            }
        };
        var runtimeMessage = new ChatMessage(ChatRole.User, "compacted-runtime-handler-message");

        // Act
        await handler.PersistAsync(new ChatHistoryPersistenceContext(
            conversation.Id,
            [uiMessage],
            [runtimeMessage],
            null));

        var uiMessages = await repository.ListMessagesWithUIAsync(conversation.Id);
        var runtimeMessages = await repository.ListRuntimeMessagesAsync(conversation.Id);

        // Assert
        Assert.HasCount(1, uiMessages);
        Assert.AreEqual("full-ui-handler-message", uiMessages[0].Content);
        Assert.HasCount(1, runtimeMessages);
        Assert.Contains("compacted-runtime-handler-message", runtimeMessages[0].ModelContent);
        Assert.DoesNotContain("full-ui-handler-message", runtimeMessages[0].ModelContent);
    }

    [TestMethod]
    public async Task RuntimeMessages_WhenUiMessagesExist_ReturnsOnlyRuntimeHistory()
    {
        // Arrange
        databasePath = Path.Combine(Path.GetTempPath(), $"narutocode-runtime-{Guid.NewGuid():N}.db");
        var connectionFactory = new SqliteConnectionFactory(databasePath);
        var initializer = new DbInitializer(connectionFactory);
        await initializer.InitializeAsync();

        var repository = new ConversationRepository(connectionFactory);
        var coordinator = new ConversationRepositoryCoordinator(connectionFactory);
        var conversation = await repository.CreateForWorkDirectoryAsync(Path.GetTempPath());

        var uiMessage = new ChatMessage(ChatRole.User, "full-ui-history-message")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["NarutoCode.IsUserInput"] = true
            }
        };
        var runtimeMessage = new ChatMessage(ChatRole.User, "compacted-runtime-history-message");

        // Act
        await coordinator.AddAsync(conversation.Id, [uiMessage]);
        await coordinator.ReplaceRuntimeMessagesAsync(conversation.Id, [runtimeMessage]);

        var uiMessages = await repository.ListMessagesWithUIAsync(conversation.Id);
        var runtimeMessages = await repository.ListRuntimeMessagesAsync(conversation.Id);

        // Assert
        Assert.HasCount(1, uiMessages);
        Assert.AreEqual("full-ui-history-message", uiMessages[0].Content);
        Assert.HasCount(1, runtimeMessages);
        Assert.AreEqual("user", runtimeMessages[0].Role);
        Assert.Contains("compacted-runtime-history-message", runtimeMessages[0].ModelContent);
        Assert.DoesNotContain("full-ui-history-message", runtimeMessages[0].ModelContent);
    }
}
