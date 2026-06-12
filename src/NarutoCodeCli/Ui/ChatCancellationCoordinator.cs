namespace NarutoCodeCli.Ui;

/// <summary>
/// 协调 TUI 运行期间的 Ctrl+C 行为，区分取消当前 Agent 请求和退出应用。
/// </summary>
internal sealed class ChatCancellationCoordinator
{
    private readonly object syncRoot = new();
    private CancellationTokenSource? currentOperationCancellationTokenSource;

    /// <summary>
    /// 注册当前正在运行的 Agent 请求取消源。
    /// </summary>
    /// <param name="cancellationTokenSource">当前请求的取消源。</param>
    public void RegisterOperation(CancellationTokenSource cancellationTokenSource)
    {
        ArgumentNullException.ThrowIfNull(cancellationTokenSource);

        lock (syncRoot)
        {
            currentOperationCancellationTokenSource = cancellationTokenSource;
        }
    }

    /// <summary>
    /// 清理当前请求取消源。
    /// </summary>
    /// <param name="cancellationTokenSource">需要清理的取消源。</param>
    public void ClearOperation(CancellationTokenSource cancellationTokenSource)
    {
        ArgumentNullException.ThrowIfNull(cancellationTokenSource);

        lock (syncRoot)
        {
            if (ReferenceEquals(currentOperationCancellationTokenSource, cancellationTokenSource))
            {
                currentOperationCancellationTokenSource = null;
            }
        }
    }

    /// <summary>
    /// 尝试取消当前正在运行的 Agent 请求。
    /// </summary>
    /// <returns>成功取消当前请求时返回 <see langword="true" />；没有运行中请求时返回 <see langword="false" />。</returns>
    public bool TryCancelCurrentOperation()
    {
        CancellationTokenSource? cancellationTokenSource;
        lock (syncRoot)
        {
            cancellationTokenSource = currentOperationCancellationTokenSource;
        }

        if (cancellationTokenSource is null || cancellationTokenSource.IsCancellationRequested)
        {
            return false;
        }

        cancellationTokenSource.Cancel();
        return true;
    }
}
