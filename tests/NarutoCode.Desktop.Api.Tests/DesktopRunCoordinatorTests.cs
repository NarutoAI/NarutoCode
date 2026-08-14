using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Entities;
using NarutoCode.Domain.Enums;
using NarutoCode.Domain.Messages;
using NarutoCode.Desktop.Api.Runs;
using NarutoCode.Desktop.Api.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace NarutoCode.Desktop.Api.Tests;

/// <summary>
/// Run 协调器状态转换测试。
/// </summary>
[TestClass]
public sealed class DesktopRunCoordinatorTests
{
    /// <summary>
    /// Queued → Running → Completed 正常路径。
    /// </summary>
    [TestMethod]
    public async Task StartAsync_NormalCompletion_TransitionsToCompleted()
    {
        var service = new FakeConversationService();
        service.ScriptMessages =
        [
            new AgentMessage(AgentMessageType.Content, "hello"),
        ];
        var coordinator = new DesktopRunCoordinator(service, new FakeConversationRepository(), new DesktopWorkspaceContextAccessor(), NullLogger<DesktopRunCoordinator>.Instance);

        var run = await coordinator.StartAsync(new ConversationSessionId(1),
            new AgentMessage(AgentMessageType.Content, "hi"), default);

        // 等待后台泵完成
        await Task.Delay(200);
        Assert.AreEqual(DesktopRunStatus.Completed, run.Status);
    }

    /// <summary>
    /// Run 在订阅建立前完成时，仍可读取正文和完成终态事件。
    /// </summary>
    [TestMethod]
    public async Task ReadEventsAsync_AfterRunCompleted_ReturnsTerminalEvent()
    {
        var service = new FakeConversationService
        {
            ScriptMessages = [new AgentMessage(AgentMessageType.Content, "hello")]
        };
        var coordinator = new DesktopRunCoordinator(service, new FakeConversationRepository(), new DesktopWorkspaceContextAccessor(), NullLogger<DesktopRunCoordinator>.Instance);

        var run = await coordinator.StartAsync(new ConversationSessionId(1),
            new AgentMessage(AgentMessageType.Content, "hi"), default);
        await Task.Delay(100);

        var events = new List<RunEvent>();
        await foreach (var item in coordinator.ReadEventsAsync(run.RunId, default))
        {
            events.Add(item);
        }

        CollectionAssert.AreEqual(
            new[] { "message.delta", "run.completed" },
            events.Select(item => item.EventType).ToArray());
    }

    /// <summary>
    /// 非正文 Agent 消息不会映射为消息正文。
    /// </summary>
    [TestMethod]
    public async Task ReadEventsAsync_NonContentMessages_UseDedicatedEventTypes()
    {
        var service = new FakeConversationService
        {
            ScriptMessages =
            [
                new AgentMessage(AgentMessageType.Temporary, "preparing"),
                new AgentMessage(AgentMessageType.ToolCall, "mode_get"),
                new AgentMessage(AgentMessageType.Usage, "42"),
            ]
        };
        var coordinator = new DesktopRunCoordinator(service, new FakeConversationRepository(), new DesktopWorkspaceContextAccessor(), NullLogger<DesktopRunCoordinator>.Instance);

        var run = await coordinator.StartAsync(new ConversationSessionId(1),
            new AgentMessage(AgentMessageType.Content, "hi"), default);
        var eventTypes = new List<string>();
        await foreach (var item in coordinator.ReadEventsAsync(run.RunId, default))
        {
            eventTypes.Add(item.EventType);
        }

        CollectionAssert.AreEqual(
            new[] { "status.updated", "tool.started", "usage.updated", "run.completed" },
            eventTypes.ToArray());
    }

    /// <summary>
    /// 同一会话重复启动 Run 抛出 RunAlreadyActiveException。
    /// </summary>
    [TestMethod]
    public async Task StartAsync_DuplicateActiveRun_ThrowsAlreadyActive()
    {
        var service = new FakeConversationService();
        // 永不完成的流，保持活跃
        service.ScriptMessages = [new AgentMessage(AgentMessageType.Content, "working")];
        service.NeverComplete = true;
        var coordinator = new DesktopRunCoordinator(service, new FakeConversationRepository(), new DesktopWorkspaceContextAccessor(), NullLogger<DesktopRunCoordinator>.Instance);

        await coordinator.StartAsync(new ConversationSessionId(1),
            new AgentMessage(AgentMessageType.Content, "hi"), default);

        await Assert.ThrowsExactlyAsync<RunAlreadyActiveException>(() =>
            coordinator.StartAsync(new ConversationSessionId(1),
                new AgentMessage(AgentMessageType.Content, "again"), default));
    }

    /// <summary>
    /// 取消 Run 后状态变为 Cancelled。
    /// </summary>
    [TestMethod]
    public async Task CancelAsync_MarksRunCancelled()
    {
        var service = new FakeConversationService();
        service.ScriptMessages = [new AgentMessage(AgentMessageType.Content, "working")];
        service.NeverComplete = true;
        var coordinator = new DesktopRunCoordinator(service, new FakeConversationRepository(), new DesktopWorkspaceContextAccessor(), NullLogger<DesktopRunCoordinator>.Instance);

        var run = await coordinator.StartAsync(new ConversationSessionId(1),
            new AgentMessage(AgentMessageType.Content, "hi"), default);

        await coordinator.CancelAsync(run.RunId, default);
        Assert.AreEqual(DesktopRunStatus.Cancelled, run.Status);
    }

    /// <summary>
    /// Error 消息使 Run 进入 Failed 状态。
    /// </summary>
    [TestMethod]
    public async Task PumpAsync_OnError_TransitionsToFailed()
    {
        var service = new FakeConversationService();
        service.ScriptMessages =
        [
            new AgentMessage(AgentMessageType.Error, "boom"),
        ];
        var coordinator = new DesktopRunCoordinator(service, new FakeConversationRepository(), new DesktopWorkspaceContextAccessor(), NullLogger<DesktopRunCoordinator>.Instance);

        var run = await coordinator.StartAsync(new ConversationSessionId(1),
            new AgentMessage(AgentMessageType.Content, "hi"), default);

        await Task.Delay(200);
        Assert.AreEqual(DesktopRunStatus.Failed, run.Status);
    }

    /// <summary>
    /// 假会话服务，按脚本输出消息。
    /// </summary>
    private sealed class FakeConversationRepository : IConversationRepository
    {
        public Task<Conversation?> GetByIdAsync(long conversationId, CancellationToken ct = default)
            => Task.FromResult<Conversation?>(new Conversation { Id = conversationId, WorkDirectory = Path.GetTempPath() });
        public Task<Conversation> GetOrCreateByWorkDirectoryAsync(string workDirectory, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversationSummary>> ListByWorkDirectoryAsync(string workDirectory, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WorkspaceSummary> GetOrCreateWorkspaceAsync(string workDirectory, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversationSummary>> ListByProjectIdAsync(long projectId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkspaceSummary>> ListWorkspacesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Conversation> CreateForWorkDirectoryAsync(string workDirectory, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Conversation> CreateForProjectIdAsync(long projectId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Conversation> CreateForProjectIdAsync(long projectId, ConversationSource source, string sourceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Conversation> GetOrCreateBySourceAsync(long projectId, ConversationSource source, string sourceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Message>> ListMessagesWithUIAsync(long conversationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Message>> ListMessagesAsync(long conversationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Message>> ListRuntimeMessagesAsync(long conversationId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeConversationService : IConversationService
    {
        public IReadOnlyList<AgentMessage> ScriptMessages { get; set; } = [];
        public bool NeverComplete { get; set; }

        public Task<ConversationHistory> LoadWorkspaceHistoryAsync(string workDirectory, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ConversationSummary>> ListWorkspaceConversationsAsync(string workDirectory, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConversationSummary>>([]);

        public Task<WorkspaceSummary> GetOrCreateWorkspaceAsync(string workDirectory, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ConversationSummary>> ListProjectConversationsAsync(long projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConversationSummary>>([]);

        public Task<IReadOnlyList<WorkspaceSummary>> ListWorkspacesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WorkspaceSummary>>([]);

        public Task<OpenWorkspaceResult> OpenWorkspaceAsync(string workDirectory, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OpenWorkspaceResult> OpenWorkspaceBySourceAsync(string workDirectory, ConversationSource source, string sourceId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ConversationSessionId> GetOrCreateSessionIdBySourceAsync(string workDirectory, ConversationSource source, string sourceId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ConversationHistory> CreateWorkspaceConversationAsync(string workDirectory, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ConversationHistory> CreateProjectConversationAsync(long projectId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ConversationHistory> LoadConversationHistoryAsync(ConversationSessionId conversationId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<AgentMessage> SendMessageAsync(
            ConversationSessionId sessionId, AgentMessage message,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var msg in ScriptMessages)
            {
                yield return msg;
            }

            if (NeverComplete)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
        }

        public Task ResetRuntimeSessionAsync(ConversationSessionId sessionId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
