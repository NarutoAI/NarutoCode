using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NarutoCode.Domain;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// 解析 LocalCodeAct 执行器使用的 Python 解释器路径，避免硬编码平台特定路径。
/// </summary>
internal static class PythonExecutableResolver
{
    /// <summary>
    /// 按优先级解析 Python 解释器路径：显式配置 → 虚拟环境 → PATH → 平台默认路径。
    /// </summary>
    /// <param name="logger">日志器，用于提示显式配置的路径不可用。</param>
    /// <returns>可执行的 Python 解释器路径。</returns>
    public static string Resolve(ILogger logger)
    {
        // 1. 显式配置优先：用户可在 config.json 的 system.pythonExecutablePath 中指定解释器（如 venv/conda 内的 Python）。
        var configured = AppData.Config.System.PythonExecutablePath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (File.Exists(configured))
            {
                return configured;
            }

            Log.PythonExecutableConfiguredNotFound(logger, configured);
        }

        // 2. 虚拟环境优先：识别 VIRTUAL_ENV / CONDA_PREFIX 指向的解释器，命中用户当前激活的环境。
        var venvPython = ResolveFromVirtualEnvironment();
        if (venvPython is not null)
        {
            return venvPython;
        }

        // 3. PATH 查找：python3 优先于 python，避免命中遗留的 Python 2。
        if (TryFindOnPath("python3", out var python3))
        {
            return python3;
        }

        if (TryFindOnPath("python", out var python))
        {
            return python;
        }

        // 4. 平台兜底：Unix 使用系统自带路径，Windows 使用 py 启动器由系统解析。
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "py"
            : "/usr/bin/python3";
    }

    /// <summary>
    /// 从当前激活的虚拟环境（venv/conda）中定位解释器，未激活任何环境时返回 null。
    /// </summary>
    private static string? ResolveFromVirtualEnvironment()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // VIRTUAL_ENV：标准 venv 创建的虚拟环境。
        var venv = Environment.GetEnvironmentVariable("VIRTUAL_ENV");
        if (!string.IsNullOrWhiteSpace(venv))
        {
            var candidate = isWindows
                ? Path.Combine(venv, "Scripts", "python.exe")
                : Path.Combine(venv, "bin", "python3");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // CONDA_PREFIX：conda 环境，解释器位置与 venv 一致（Windows 下直接在环境根目录）。
        var condaPrefix = Environment.GetEnvironmentVariable("CONDA_PREFIX");
        if (!string.IsNullOrWhiteSpace(condaPrefix))
        {
            var candidate = isWindows
                ? Path.Combine(condaPrefix, "python.exe")
                : Path.Combine(condaPrefix, "bin", "python3");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 在 PATH 环境变量中查找指定可执行文件，返回完整路径。
    /// </summary>
    private static bool TryFindOnPath(string name, out string fullPath)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            fullPath = string.Empty;
            return false;
        }

        // Windows 下可执行文件带扩展名，Unix 下无扩展名。
        var exts = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { ".exe", ".cmd", ".bat", string.Empty }
            : new[] { string.Empty };
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir))
            {
                continue;
            }

            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate))
                {
                    fullPath = candidate;
                    return true;
                }
            }
        }

        fullPath = string.Empty;
        return false;
    }
}
