using NarutoCode.Domain.Workspaces;
namespace NarutoCode.Gateway.Hosting;
/// <summary>
/// 按异步消息流切换根工作目录的访问器。
/// </summary>
internal sealed class GatewayWorkspaceContextAccessor : IWorkspaceContextAccessor
{
    private readonly AsyncLocal<WorkspaceContext?> _current = new();
    public WorkspaceContext Current => _current.Value ?? throw new InvalidOperationException("当前 Gateway 消息未绑定工作目录。");
    internal IDisposable Push(string workspace) { var previous = _current.Value; _current.Value = new WorkspaceContext(workspace); return new Scope(this, previous); }
    private sealed class Scope(GatewayWorkspaceContextAccessor owner, WorkspaceContext? previous) : IDisposable { public void Dispose() => owner._current.Value = previous; }
}
