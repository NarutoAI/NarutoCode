using NarutoCode.Domain.Workspaces;

namespace NarutoCodeCli.Workspaces;

/// <summary>
/// CLI 工作区上下文访问器，保存程序启动瞬间捕获到的用户工作目录。
/// </summary>
internal sealed class CliWorkspaceContextAccessor : IWorkspaceContextAccessor
{
    /// <summary>
    /// 创建 CLI 工作区上下文访问器。
    /// </summary>
    /// <param name="workspaceContext">程序启动时捕获的工作区上下文。</param>
    public CliWorkspaceContextAccessor(WorkspaceContext workspaceContext)
    {
        Current = workspaceContext ?? throw new ArgumentNullException(nameof(workspaceContext));
    }

    /// <inheritdoc />
    public WorkspaceContext Current { get; }
}
