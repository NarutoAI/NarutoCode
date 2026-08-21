using NarutoCode.Domain.Workspaces;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// 将会话 Runtime 绑定到固定工作目录，避免缓存 Agent 在 AsyncLocal scope 切换后读错目录。
/// </summary>
internal sealed class FixedWorkspaceContextAccessor(WorkspaceContext workspaceContext) : IWorkspaceContextAccessor
{
    /// <inheritdoc />
    public WorkspaceContext Current { get; } = workspaceContext;
}
