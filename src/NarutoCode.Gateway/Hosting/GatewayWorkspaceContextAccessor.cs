using NarutoCode.Domain.Workspaces;

namespace NarutoCode.Gateway.Hosting;

/// <summary>
/// 固定工作目录访问器，从 gateway.json 的 workspace 字段构造上下文。
/// </summary>
internal sealed class GatewayWorkspaceContextAccessor : IWorkspaceContextAccessor
{
    /// <param name="workspace">网关配置的固定工作目录。</param>
    internal GatewayWorkspaceContextAccessor(string workspace)
    {
        Current = new WorkspaceContext(workspace);
    }

    /// <summary>
    /// 当前工作区上下文。
    /// </summary>
    public WorkspaceContext Current { get; }
}
