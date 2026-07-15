using NarutoCode.Domain.Workspaces;

namespace NarutoCode.Desktop.Api.Workspaces;

/// <summary>
/// 为 Desktop API 提供按异步 Run 隔离的工作区上下文。
/// </summary>
internal sealed class DesktopWorkspaceContextAccessor : IWorkspaceContextAccessor
{
    private readonly AsyncLocal<WorkspaceContext?> current = new();
    private readonly WorkspaceContext defaultContext = new(Environment.CurrentDirectory);

    /// <inheritdoc />
    public WorkspaceContext Current => current.Value ?? defaultContext;

    /// <summary>
    /// 在当前异步执行流中切换工作目录，并在释放后恢复原上下文。
    /// </summary>
    /// <param name="workDirectory">当前 Run 所属会话的工作目录。</param>
    /// <returns>用于恢复前一个上下文的作用域。</returns>
    public IDisposable Push(string workDirectory)
    {
        var previous = current.Value;
        current.Value = new WorkspaceContext(workDirectory);
        return new WorkspaceContextScope(current, previous);
    }

    private sealed class WorkspaceContextScope(
        AsyncLocal<WorkspaceContext?> current,
        WorkspaceContext? previous) : IDisposable
    {
        public void Dispose()
        {
            current.Value = previous;
        }
    }
}
