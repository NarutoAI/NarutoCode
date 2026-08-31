using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.Logging;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// <see cref="IShellExecutorFactory"/> 的默认实现：会话级 Shell 执行器工厂。
/// <para>
/// 内部跟踪本会话内创建的所有 <see cref="ShellExecutor"/>；
/// 会话结束（<see cref="ConversationAgentRuntime.DisposeAsync"/> 触发 <see cref="DisposeAsync"/>）时统一释放，
/// 回收本会话持有的全部 Shell 子进程，单个 Shell 释放失败不影响其余。
/// </para>
/// <para>
/// 适配当前运行平台选择 Shell（Windows 优先 pwsh、回退 cmd 的无状态模式；Unix 使用 bash/sh），
/// 后续如需按宿主/沙箱切换实现，在此处分支即可，调用方无需改动。
/// </para>
/// </summary>
internal sealed class ShellExecutorFactory(ILoggerFactory loggerFactory) : IShellExecutorFactory, IAsyncDisposable
{
    // 本会话内所有创建的 Shell 句柄；会话结束时由 DisposeAsync 统一回收
    private readonly List<ShellExecutor> _shells = [];

    // 跟踪列表锁：子 Agent 并行委派会并发创建 Shell，List.Add 非线程安全
    private readonly Lock _shellLock = new();

    private readonly ILogger _logger = loggerFactory.CreateLogger<ShellExecutorFactory>();
    private int _disposed;

    /// <inheritdoc />
    public ShellExecutor Create(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var resolvedShell = ResolvedShell();
        //校验是否存在docker
        if (IsExistsDocker(resolvedShell.shell))
        {
            return Track(new DockerShellExecutor(new DockerShellExecutorOptions
            {
                Mode = resolvedShell.model,
            }), workingDirectory);
        }

        return Track(new LocalShellExecutor(new LocalShellExecutorOptions
        {
            Mode = resolvedShell.model,
            Shell = resolvedShell.shell,
            AcknowledgeUnsafe = true,
            WorkingDirectory = Path.GetFullPath(workingDirectory),
            ConfineWorkingDirectory = true
        }), workingDirectory);
    }

    /// <summary>
    /// 将创建的 Shell 纳入会话跟踪并记录日志；会话结束时由 <see cref="DisposeAsync"/> 统一回收。
    /// </summary>
    private ShellExecutor Track(ShellExecutor shell, string workingDirectory)
    {
        lock (_shellLock)
        {
            _shells.Add(shell);
        }

        Log.ShellExecutorCreated(_logger, workingDirectory);
        return shell;
    }

    /// <summary>
    /// 归还并释放 Shell：先从跟踪列表移除引用，再关闭底层子进程；
    /// Shell 已不在跟踪列表（重复归还）时直接返回，保证幂等。
    /// </summary>
    public async ValueTask ReleaseAsync(ShellExecutor shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        // 先移出跟踪再释放：已释放的 Shell 不会在跟踪列表残留引用；重复归还时移除失败直接忽略
        lock (_shellLock)
        {
            if (!_shells.Remove(shell))
            {
                return;
            }
        }

        try
        {
            await shell.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 单个 Shell 释放失败仅记录日志，不影响调用方流程
            Log.ShellExecutorDisposeFailed(_logger, ex);
        }
    }

    /// <summary>
    /// 释放本会话内跟踪的全部 Shell 子进程；单个 Shell 释放失败仅记录日志，不影响其余，保证最大努力回收语义。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // 幂等保护：重复调用直接返回
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // 复制后清空，避免释放过程中被反向调用时出现重复释放
        ShellExecutor[] shells;
        lock (_shellLock)
        {
            shells = [.. _shells];
            _shells.Clear();
        }

        if (shells.Length == 0)
        {
            return;
        }

        foreach (var shell in shells)
        {
            try
            {
                await shell.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 单个 Shell 释放失败仅记录日志，不影响其余 Shell 与整体流程
                Log.ShellExecutorDisposeFailed(_logger, ex);
            }
        }

        // 全部释放完成后记录数量，便于排查泄漏
        Log.ShellExecutorScopeDisposed(_logger, shells.Length);
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
