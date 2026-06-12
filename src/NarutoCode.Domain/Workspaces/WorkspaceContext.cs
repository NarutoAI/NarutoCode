namespace NarutoCode.Domain.Workspaces;

/// <summary>
/// 工作区上下文，表示用户启动 NarutoCode 时所在的项目目录。
/// </summary>
public sealed record WorkspaceContext
{
    /// <summary>
    /// 创建工作区上下文。
    /// </summary>
    /// <param name="workingDirectory">用户启动程序时所在的工作目录。</param>
    /// <exception cref="ArgumentException">当工作目录为空时抛出。</exception>
    public WorkspaceContext(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException("工作目录不能为空。", nameof(workingDirectory));
        }

        WorkingDirectory = Path.GetFullPath(workingDirectory);
    }

    /// <summary>
    /// 用户启动程序时所在的工作目录。
    /// </summary>
    public string WorkingDirectory { get; }
}
