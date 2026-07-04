using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Entities;
using NarutoCode.Domain.Messages;
using NarutoCode.Infrastructure.AIAgents;

namespace NarutoCode.Infrastructure.Tests.AIAgents;

/// <summary>
/// 验证 Agent 恢复会话时的历史消息选择规则。
/// </summary>
[TestClass]
public sealed class MafAgentChatClientHistoryTests
{
    /// <summary>
    /// Runtime 历史非空时应优先使用 runtime，避免读取完整 UI 历史重新裁剪。
    /// </summary>
    [TestMethod]
    public async Task LoadSessionHistoryMessagesAsync_WhenRuntimeMessagesExist_UsesRuntimeMessagesWithoutReadingUiHistory()
    {
        // Arrange
        var runtimeMessage = CreateMessage(1, "runtime-history");
        var uiMessage = CreateMessage(2, "ui-history");
        var repository = new FakeConversationRepository([runtimeMessage], [uiMessage]);

        // Act
        var messages = await MafAgentChatClient.LoadSessionHistoryMessagesAsync(
            repository,
            new ConversationSessionId(100));

        // Assert
        Assert.HasCount(1, messages);
        Assert.AreSame(runtimeMessage, messages[0]);
        Assert.AreEqual(1, repository.ListRuntimeMessagesCallCount);
        Assert.AreEqual(0, repository.ListMessagesCallCount);
    }

    /// <summary>
    /// Runtime 历史为空时应回退 UI 历史，兼容升级前没有 runtime 快照的旧会话。
    /// </summary>
    [TestMethod]
    public async Task LoadSessionHistoryMessagesAsync_WhenRuntimeMessagesEmpty_FallsBackToUiHistory()
    {
        // Arrange
        var uiMessage = CreateMessage(2, "ui-history");
        var repository = new FakeConversationRepository([], [uiMessage]);

        // Act
        var messages = await MafAgentChatClient.LoadSessionHistoryMessagesAsync(
            repository,
            new ConversationSessionId(100));

        // Assert
        Assert.HasCount(1, messages);
        Assert.AreSame(uiMessage, messages[0]);
        Assert.AreEqual(1, repository.ListRuntimeMessagesCallCount);
        Assert.AreEqual(1, repository.ListMessagesCallCount);
    }

    private static Message CreateMessage(long id, string content)
    {
        return new Message
        {
            Id = id,
            ConversationId = 100,
            Role = "user",
            Content = content,
            ModelContent = content,
            CreatedAt = DateTime.Now,
            MessageType = AgentMessageType.Content,
            Visibility = MessageVisibility.Visible
        };
    }

    private sealed class FakeConversationRepository(
        IReadOnlyList<Message> runtimeMessages,
        IReadOnlyList<Message> uiMessages) : IConversationRepository
    {
        public int ListRuntimeMessagesCallCount { get; private set; }

        public int ListMessagesCallCount { get; private set; }

        public Task<Conversation> GetOrCreateByWorkDirectoryAsync(
            string workDirectory,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ConversationSummary>> ListByWorkDirectoryAsync(
            string workDirectory,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Conversation> CreateForWorkDirectoryAsync(
            string workDirectory,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Conversation?> GetByIdAsync(
            long conversationId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Message>> ListMessagesWithUIAsync(
            long conversationId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Message>> ListMessagesAsync(
            long conversationId,
            CancellationToken cancellationToken = default)
        {
            ListMessagesCallCount++;
            return Task.FromResult(uiMessages);
        }

        public Task<IReadOnlyList<Message>> ListRuntimeMessagesAsync(
            long conversationId,
            CancellationToken cancellationToken = default)
        {
            ListRuntimeMessagesCallCount++;
            return Task.FromResult(runtimeMessages);
        }
    }
}
