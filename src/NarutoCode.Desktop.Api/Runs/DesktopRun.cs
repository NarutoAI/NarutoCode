using System.Threading.Channels;

namespace NarutoCode.Desktop.Api.Runs;

/// <summary>
/// 单次 Run 的运行时状态，包含事件通道和取消令牌。
/// </summary>
internal sealed class DesktopRun
{
    /// <summary>Run 标识。</summary>
    public string RunId { get; }

    /// <summary>关联的会话标识。</summary>
    public long ConversationId { get; }

    /// <summary>当前状态。</summary>
    public DesktopRunStatus Status { get; set; } = DesktopRunStatus.Queued;

    /// <summary>事件序号计数器（供 Interlocked 使用）。</summary>
    private long _sequenceCounter;
    /// <summary>分配下一个事件序号。</summary>
    public long NextSequence() => Interlocked.Increment(ref _sequenceCounter);

    /// <summary>当前待审批标识。</summary>
    public string? PendingApprovalId { get; set; }

    /// <summary>事件通道读取端。</summary>
    public ChannelReader<RunEvent> Reader => _channel.Reader;

    /// <summary>事件通道写入端。</summary>
    public ChannelWriter<RunEvent> Writer => _channel.Writer;

    private readonly Channel<RunEvent> _channel;
    private readonly CancellationTokenSource _linkedCts;

    /// <summary>
    /// 创建 Run 实例。
    /// </summary>
    /// <param name="runId">Run 标识。</param>
    /// <param name="conversationId">会话标识。</param>
    /// <param name="externalCts">外部取消令牌。</param>
    public DesktopRun(string runId, long conversationId, CancellationToken externalCts)
    {
        RunId = runId;
        ConversationId = conversationId;
        _channel = Channel.CreateUnbounded<RunEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCts);
    }

    /// <summary>Run 级别的取消令牌。</summary>
    public CancellationToken RunToken => _linkedCts.Token;

    /// <summary>
    /// 取消 Run 并完成事件通道。
    /// </summary>
    public void Cancel()
    {
        _linkedCts.Cancel();
        Writer.TryComplete();
    }
}
