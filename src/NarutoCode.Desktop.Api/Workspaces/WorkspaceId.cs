using System.Security.Cryptography;
using System.Text;
using NarutoCode.Domain.Workspaces;

namespace NarutoCode.Desktop.Api.Workspaces;

/// <summary>
/// 基于工作目录路径生成确定性哈希标识，Windows 上先做大小写归一化。
/// </summary>
internal static class WorkspaceId
{
    /// <summary>
    /// 将规范化后的工作目录路径进行 SHA-256 哈希并返回小写十六进制字符串。
    /// </summary>
    /// <param name="workDirectory">工作目录路径。</param>
    /// <returns>64 字符小写十六进制哈希。</returns>
    public static string Create(string workDirectory)
    {
        var normalized = WorkspacePath.Normalize(workDirectory);
        // Windows 文件系统不区分大小写，统一转大写后再哈希
        var key = OperatingSystem.IsWindows()
            ? normalized.ToUpperInvariant()
            : normalized;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))
            .ToLowerInvariant();
    }
}
