namespace NarutoCode.Domain.Workspaces;

/// <summary>
/// 工作区上下文访问器，用于让应用层和基础设施层获取当前用户启动程序时所在的项目目录。
/// </summary>
public interface IWorkspaceContextAccessor
{
    /// <summary>
    /// 当前工作区上下文。
    /// </summary>
    WorkspaceContext Current { get; }
}
