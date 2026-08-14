using Microsoft.Extensions.Logging.Abstractions;
using NarutoCode.Domain.Messages;
using NarutoCode.Infrastructure.AIAgents;

namespace NarutoCode.Infrastructure.Tests.AIAgents;

/// <summary>
/// 验证工作目录 Agent 池的会话级隔离、串行与失效重建行为。
/// </summary>
[TestClass]
public sealed class WorkspaceAgentPoolTests
{
    /// <summary>
    /// 同一会话的第二个租约必须等待第一个租约释放，避免共享 AgentSession 与持久 Shell。
    /// </summary>
    [TestMethod]
    public async Task AcquireAsync_SameConversation_WaitsForPreviousLease()
    {
        var (pool, _, _) = CreatePool();
        var sessionId = new ConversationSessionId(1);
        await using var firstLease = await pool.AcquireAsync(sessionId);

        var secondLeaseTask = pool.AcquireAsync(sessionId).AsTask();
        await Task.Delay(80);
        Assert.IsFalse(secondLeaseTask.IsCompleted);

        await firstLease.DisposeAsync();
        await using var secondLease = await secondLeaseTask;
        Assert.IsNotNull(secondLease);
    }

    /// <summary>
    /// 同一工作目录中的不同会话必须可并发获得各自的运行时。
    /// </summary>
    [TestMethod]
    public async Task AcquireAsync_DifferentConversations_AllowsParallelLeases()
    {
        var (pool, createCount, _) = CreatePool();
        await using var firstLease = await pool.AcquireAsync(new ConversationSessionId(1));
        await using var secondLease = await pool.AcquireAsync(new ConversationSessionId(2));

        Assert.AreEqual(2, createCount.Value);
        Assert.AreEqual(2, pool.ActiveLeaseCount);
    }

    /// <summary>
    /// 会话失效后应释放旧 Shell，并在下一次获取时创建新的运行时。
    /// </summary>
    [TestMethod]
    public async Task AcquireAsync_InvalidatedConversation_RecreatesRuntimeAfterRelease()
    {
        var (pool, createCount, _) = CreatePool();
        var sessionId = new ConversationSessionId(1);
        var firstLease = await pool.AcquireAsync(sessionId);
        firstLease.Invalidate();
        await firstLease.DisposeAsync();

        await using var secondLease = await pool.AcquireAsync(sessionId);
        Assert.AreEqual(2, createCount.Value);
    }

    /// <summary>
    /// 运行中的旧租约失效后，新 Runtime 可以创建，但旧 Shell 必须等旧租约释放才关闭。
    /// </summary>
    [TestMethod]
    public async Task AcquireAsync_InvalidatedLease_DefersOldShellDisposalUntilLeaseRelease()
    {
        var (pool, createCount, shells) = CreatePool();
        var sessionId = new ConversationSessionId(1);
        var firstLease = await pool.AcquireAsync(sessionId);
        firstLease.Invalidate();

        var secondLeaseTask = pool.AcquireAsync(sessionId).AsTask();
        await Task.Delay(80);
        Assert.IsFalse(secondLeaseTask.IsCompleted);
        Assert.AreEqual(0, shells[0].DisposeCount);

        await firstLease.DisposeAsync();
        await using var secondLease = await secondLeaseTask;
        Assert.AreEqual(2, createCount.Value);
        Assert.AreEqual(1, shells[0].DisposeCount);
    }

    private static (WorkspaceAgentPool Pool, Counter CreateCount, List<TestDisposable> Shells) CreatePool()
    {
        var createCount = new Counter();
        var shells = new List<TestDisposable>();
        var pool = new WorkspaceAgentPool(
            "/tmp/narutocode-agent-pool",
            () =>
            {
                createCount.Value++;
                var shell = new TestDisposable();
                shells.Add(shell);
                return new ConversationAgentRuntime(null!, shell);
            },
            NullLogger<WorkspaceAgentPool>.Instance);
        return (pool, createCount, shells);
    }

    private sealed class Counter
    {
        public int Value { get; set; }
    }

    private sealed class TestDisposable : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
