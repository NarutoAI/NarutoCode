using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace NarutoCode.Infrastructure.Tools;

/// <summary>
/// 提供给 Agent 调用的 Bash 工具集合。
/// 这些方法可以通过 AIFunctionFactory.Create(...) 注册到 AI Agent 中，
/// 也可以被普通 C# 代码直接调用。
/// </summary>
public static class BashAgentTools
{
    /// <summary>
    /// 明显危险命令的黑名单规则。
    /// 这里只拦截高风险系统级或破坏性命令，避免误杀常规开发命令。
    /// </summary>
    private static readonly (Regex Pattern, string Reason)[] DangerousCommandRules =
    [
        (new Regex(@"(^|\s)sudo(\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "禁止执行提权命令 sudo。"),
        (new Regex(@"(^|\s)(shutdown|reboot|halt|poweroff)(\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "禁止执行关机、重启或停止系统命令。"),
        (new Regex(@"(^|\s)(mkfs(\.[^\s]+)?|fdisk)(\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "禁止执行磁盘格式化或分区命令。"),
        (new Regex(@"diskutil\s+eraseDisk", RegexOptions.IgnoreCase | RegexOptions.Compiled), "禁止执行磁盘擦除命令。"),
        (new Regex(@"rm\s+-.*r.*f.*(/\s*$|/\*|~\s*$|~/)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "禁止执行高风险递归强制删除命令。"),
        (new Regex(@"(curl|wget)[^\n\r|]*\|[^\n\r]*(sh|bash)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "禁止执行下载后通过管道直接交给 sh/bash 的命令。")
    ];

    /// <summary>
    /// 执行一段 Bash 命令，并返回退出码、标准输出、标准错误和工作目录。
    /// C# 用法：var result = await BashAgentTools.ExecuteBash("ls -la", "/tmp");
    /// </summary>
    /// <param name="command">要执行的 Bash 命令。</param>
    /// <param name="workingDirectory">命令执行目录；为空时使用默认工作目录。</param>
    /// <returns>格式化后的命令执行结果。</returns>
    [Description("执行一段 Bash 命令，可指定工作目录；如果未指定，则使用默认工作目录执行")]
    public static async Task<string> ExecuteBash(
        [Description("要执行的 Bash 命令，例如 ls -la、dotnet build")]
        string command,
        [Description("命令执行的工作目录")] string workingDirectory)
    {
        var safetyValidationResult = ValidateCommandSafety(command);
        if (!string.IsNullOrWhiteSpace(safetyValidationResult))
        {
            return safetyValidationResult;
        }

        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return "Error:workingDirectory 参数未传递";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        startInfo.ArgumentList.Add("-lc");
        startInfo.ArgumentList.Add(command);

        using var process = new Process {StartInfo = startInfo};
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        var result = new StringBuilder();
        result.AppendLine($"工作目录: {workingDirectory}");
        result.AppendLine($"退出码: {process.ExitCode}");

        if (!string.IsNullOrWhiteSpace(standardOutput))
        {
            result.AppendLine("标准输出:");
            result.AppendLine(standardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(standardError))
        {
            result.AppendLine("标准错误:");
            result.AppendLine(standardError.TrimEnd());
        }

        if (string.IsNullOrWhiteSpace(standardOutput) && string.IsNullOrWhiteSpace(standardError))
        {
            result.AppendLine("命令无输出");
        }

        return result.ToString().TrimEnd();
    }

    /// <summary>
    /// 校验命令是否命中危险指令黑名单。
    /// 命中时返回拦截说明，未命中时返回空字符串。
    /// </summary>
    /// <param name="command">待执行的 Bash 命令。</param>
    /// <returns>拦截说明或空字符串。</returns>
    private static string ValidateCommandSafety(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "命令不能为空。";
        }

        foreach (var rule in DangerousCommandRules)
        {
            if (rule.Pattern.IsMatch(command))
            {
                return $"命令被安全策略拦截：{rule.Reason}\n原始命令: {command}";
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 返回该工具类在 Agent 注册和普通 C# 调用中的使用示例。
    /// </summary>
    /// <returns>多行文本示例。</returns>
    public static string GetUsageExamples()
    {
        return """
               Agent 注册示例:
               var tools = new List<AITool>
               {
                   AIFunctionFactory.Create(BashAgentTools.ExecuteBash)
               };

               普通 C# 调用示例:
               var result1 = await BashAgentTools.ExecuteBash("pwd");
               var result2 = await BashAgentTools.ExecuteBash("ls -la", "/Users/zhanghaibo/Projects/我的/learn-demo/Microsoft.Agents.AI-Test");
               var result3 = await BashAgentTools.ExecuteBash("dotnet build", "/Users/zhanghaibo/Projects/我的/learn-demo/Microsoft.Agents.AI-Test");
               """;
    }
}