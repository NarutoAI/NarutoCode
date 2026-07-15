namespace NarutoCode.Desktop.Api.Runs;

/// <summary>
/// 指定的 Run 不存在时抛出。
/// </summary>
internal sealed class RunNotFoundException(string runId)
    : InvalidOperationException($"Run {runId} 不存在。")
{
    /// <summary>未找到的 Run 标识。</summary>
    public string RunId { get; } = runId;
}
