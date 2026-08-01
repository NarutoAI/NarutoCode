using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Agents.AI.Tools.Shell;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// 创建适配当前运行平台的本地 Shell 执行器。
/// </summary>
internal static class ShellExecutorFactory
{
    /// <summary>
    /// 创建本地 Shell 执行器，Windows 下避免 cmd.exe 不支持持久模式导致启动失败。
    /// </summary>
    /// <returns>本地 Shell 执行器。</returns>
    public static ShellExecutor Create(string? workingDirectory = null)
    {
        var resolvedShell = ResolvedShell();
        //校验是否存在docker 
        if (IsExistsDocker(resolvedShell.shell))
        {
            return new DockerShellExecutor(new DockerShellExecutorOptions
            {
                Mode = resolvedShell.model,
            });
        }

        return new LocalShellExecutor(new LocalShellExecutorOptions
        {
            Mode = resolvedShell.model,
            Shell = resolvedShell.shell,
            AcknowledgeUnsafe = true,
            WorkingDirectory = workingDirectory is null ? null : Path.GetFullPath(workingDirectory),
            ConfineWorkingDirectory = !string.IsNullOrWhiteSpace(workingDirectory)
        });
    }

    private static bool IsExistsDocker(string binary )
    {
        return false;//todo 后面处理
        var startInfo = new ProcessStartInfo
        {
            FileName = binary,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (binary.Contains("pwsh") || binary.Contains("powershell"))
        {
            startInfo.ArgumentList.Add("-Command");
        }
        else if (binary.Contains("cmd"))
        {
            startInfo.ArgumentList.Add("/c");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
        }
      
        startInfo.ArgumentList.Add("docker --version");
        using var process = Process.Start(startInfo);

        try
        {
            if (process is null)
            {
                return false;
            }

            var error = process.StandardError.ReadToEnd();

            process.WaitForExit();
            
            if (process.ExitCode != 0)
            {
                return false;
            }

            return !(error.Length > 0);
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    //代码参考  ShellResolver
    private static (ShellMode model, string shell) ResolvedShell()
    {
        // 如果是window的场景的话，cmd的时候设置非持久化 因为cmd终端 不支持持久化写入
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (TryFindOnPath("pwsh", out var pwsh))
            {
                return (ShellMode.Persistent, pwsh);
            }

            if (TryFindOnPath("powershell", out var winps))
            {
                return (ShellMode.Persistent, winps);
            }

            return (ShellMode.Stateless, Path.Combine(SystemRoot(), "System32", "cmd.exe"));
        }

        if (File.Exists("/bin/bash"))
        {
            return (ShellMode.Persistent, "/bin/bash");
        }

        return (ShellMode.Persistent, "/bin/sh");
    }

    private static string SystemRoot() =>
        Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";

    private static bool TryFindOnPath(string name, out string fullPath)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            fullPath = string.Empty;
            return false;
        }

        var exts = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] {".exe", ".cmd", ".bat", string.Empty}
            : new[] {string.Empty};
        foreach (var dir in pathEnv!.Split(Path.PathSeparator))
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