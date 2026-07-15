using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Messages;
using NarutoCode.Desktop.Api.Runs;
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
        var coordinator = new DesktopRunCoordinator(service, NullLogger<DesktopRunCoordinator>.Instance);

        var run = await coordinator.StartAsync(new ConversationSessionId(1),
            new AgentMessage(AgentMessageType.Content, "hi"), default);

        // 等待后台泵完成
        await Task.Delay(200);
        Assert.AreEqual(DesktopRunStatus.Completed, run.Status);
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
        var coordinator = new DesktopRunCoordinator(service, NullLogger<DesktopRunCoordinator>.Instance);

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
        var coordinator = new DesktopRunCoordinator(service, NullLogger<DesktopRunCoordinator>.Instance);

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
        var coordinator = new DesktopRunCoordinator(service, NullLogger<DesktopRunCoordinator>.Instance);

        var run = await coordinator.StartAsync(new ConversationSessionId(1),
            new AgentMessage(AgentMessageType.Content, "hi"), default);

        await Task.Delay(200);
        Assert.AreEqual(DesktopRunStatus.Failed, run.Status);
    }

    /// <summary>
    /// 假会话服务，按脚本输出消息。
    /// </summary>
    private sealed class FakeConversationService : IConversationService
    {
        public IReadOnlyList<AgentMessage> ScriptMessages { get; set; } = [];
        public bool NeverComplete { get; set; }

        public Task<ConversationHistory> LoadWorkspaceHistoryAsync(string workDirectory, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ConversationSummary>> ListWorkspaceConversationsAsync(string workDirectory, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConversationSummary>>([]);

        public Task<IReadOnlyList<WorkspaceSummary>> ListWorkspacesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WorkspaceSummary>>([]);

        public Task<OpenWorkspaceResult> OpenWorkspaceAsync(string workDirectory, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ConversationHistory> CreateWorkspaceConversationAsync(string workDirectory, CancellationToken ct = default)
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
