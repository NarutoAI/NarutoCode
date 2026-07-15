namespace NarutoCode.Domain.Workspaces;

/// <summary>
/// 提供跨宿主一致的工作目录规范化规则。
/// </summary>
public static class WorkspacePath
{
    /// <summary>
    /// 将工作目录转换为绝对规范路径。
    /// </summary>
    /// <param name="workDirectory">待规范化的工作目录。</param>
    /// <returns>移除非根目录末尾分隔符后的绝对路径。</returns>
    public static string Normalize(string workDirectory)
    {
        if (string.IsNullOrWhiteSpace(workDirectory))
        {
            throw new ArgumentException("工作目录不能为空。", nameof(workDirectory));
        }

        var fullPath = Path.GetFullPath(workDirectory);
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.Ordinal)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
