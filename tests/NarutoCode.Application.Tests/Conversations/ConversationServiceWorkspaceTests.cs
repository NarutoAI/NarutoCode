using NarutoCode.Application.Agents;
using NarutoCode.Application.Conversations;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Entities;
using NarutoCode.Domain.Enums;
using NarutoCode.Domain.Messages;

namespace NarutoCode.Application.Tests.Conversations;

/// <summary>
/// 验证工作区路径规范化和幂等打开行为。
/// </summary>
[TestClass]
public sealed class ConversationServiceWorkspaceTests
{
    /// <summary>
    /// 已有会话时应加载最近会话，且仓储收到规范化路径。
    /// </summary>
    [TestMethod]
    public async Task OpenWorkspaceAsync_WhenWorkspaceExists_LoadsLatestConversation()
    {
        var repository = new FakeConversationRepository(hasExistingConversation: true);
        var service = new ConversationService(new FakeAgentChatClient(), repository);
        var input = Path.Combine(Path.GetTempPath(), "workspace", ".");

        var result = await service.OpenWorkspaceAsync(input);

        Assert.AreEqual(Path.GetFullPath(input), repository.RequestedWorkDirectory);
        Assert.IsFalse(result.Created);
        Assert.AreEqual(repository.ExistingConversationId, result.History.SessionId.Value);
    }

    /// <summary>
    /// 不存在会话时应只创建一次首个会话。
    /// </summary>
    [TestMethod]
    public async Task OpenWorkspaceAsync_WhenWorkspaceDoesNotExist_CreatesFirstConversation()
    {
        var repository = new FakeConversationRepository(hasExistingConversation: false);
        var service = new ConversationService(new FakeAgentChatClient(), repository);

        var result = await service.OpenWorkspaceAsync(Path.GetTempPath());

        Assert.IsTrue(result.Created);
        Assert.AreEqual(1, repository.CreateCallCount);
    }

    private sealed class FakeAgentChatClient : IAgentChatClient
    {
        public async IAsyncEnumerable<AgentMessage> SendMessageAsync(
            ConversationSessionId sessionId,
            AgentMessage message,
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task ResetRuntimeSessionAsync(
            ConversationSessionId sessionId,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConversationRepository(bool hasExistingConversation) : IConversationRepository
    {
        public long ExistingConversationId { get; } = 9_007_199_254_740_993;

        public string? RequestedWorkDirectory { get; private set; }

        public int CreateCallCount { get; private set; }

        public Task<Conversation> GetOrCreateByWorkDirectoryAsync(
            string workDirectory,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ConversationSummary>> ListByWorkDirectoryAsync(
            string workDirectory,
            CancellationToken cancellationToken = default)
        {
            RequestedWorkDirectory = workDirectory;
            IReadOnlyList<ConversationSummary> result = hasExistingConversation
                ? [CreateSummary(ExistingConversationId)]
                : [];
            return Task.FromResult(result);
        }

        public Task<WorkspaceSummary> GetOrCreateWorkspaceAsync(
            string workDirectory,
            CancellationToken cancellationToken = default)
        {
            RequestedWorkDirectory = workDirectory;
            return Task.FromResult(new WorkspaceSummary(7, "workspace", workDirectory, 0, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, 0));
        }

        public Task<IReadOnlyList<ConversationSummary>> ListByProjectIdAsync(
            long projectId,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ConversationSummary> result = hasExistingConversation
                ? [CreateSummary(ExistingConversationId)]
                : [];
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<WorkspaceSummary>> ListWorkspacesAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkspaceSummary>>([]);
        }

        public Task<Conversation> CreateForWorkDirectoryAsync(
            string workDirectory,
            CancellationToken cancellationToken = default)
        {
            CreateCallCount++;
            RequestedWorkDirectory = workDirectory;
            return Task.FromResult(new Conversation
            {
                Id = ExistingConversationId,
                WorkDirectory = workDirectory
            });
        }

        public Task<Conversation> CreateForProjectIdAsync(
            long projectId,
            CancellationToken cancellationToken = default)
        {
            CreateCallCount++;
            return Task.FromResult(new Conversation
            {
                Id = ExistingConversationId,
                ProjectId = projectId,
                WorkDirectory = RequestedWorkDirectory ?? string.Empty
            });
        }

        public Task<Conversation> CreateForProjectIdAsync(
            long projectId,
            ConversationSource source,
            string sourceId,
            CancellationToken cancellationToken = default)
        {
            CreateCallCount++;
            return Task.FromResult(new Conversation
            {
                Id = ExistingConversationId,
                ProjectId = projectId,
                WorkDirectory = RequestedWorkDirectory ?? string.Empty,
                Source = source,
                SourceId = sourceId
            });
        }

        public Task<Conversation> GetOrCreateBySourceAsync(
            long projectId,
            ConversationSource source,
            string sourceId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new Conversation
            {
                Id = ExistingConversationId,
                ProjectId = projectId,
                WorkDirectory = RequestedWorkDirectory ?? string.Empty,
                Source = source,
                SourceId = sourceId
            });
        }

        public Task<Conversation?> GetByIdAsync(
            long conversationId,
            CancellationToken cancellationToken = default)
        {
            Conversation? conversation = new()
            {
                Id = conversationId,
                WorkDirectory = RequestedWorkDirectory ?? string.Empty
            };
            return Task.FromResult(conversation);
        }

        public Task<IReadOnlyList<Message>> ListMessagesWithUIAsync(
            long conversationId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Message>>([]);
        }

        public Task<IReadOnlyList<Message>> ListMessagesAsync(
            long conversationId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Message>>([]);
        }

        public Task<IReadOnlyList<Message>> ListRuntimeMessagesAsync(
            long conversationId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Message>>([]);
        }

        private static ConversationSummary CreateSummary(long id)
        {
            return new ConversationSummary(
                id,
                "workspace",
                DateTime.UtcNow,
                DateTime.UtcNow,
                0,
                0,
                0,
                string.Empty);
        }
    }
}
