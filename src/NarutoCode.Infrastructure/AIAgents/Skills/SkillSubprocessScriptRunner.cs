using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.AI;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.AIAgents.Skills;

/// <summary>
/// script 脚本执行器 fork maf
/// </summary>
internal static class SkillSubprocessScriptRunner
{
    /// <summary>
    /// 执行script脚本
    /// </summary>
    /// <param name="skill"></param>
    /// <param name="script"></param>
    /// <param name="arguments"></param>
    /// <param name="serviceProvider"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static async Task<object?> RunAsync(
        AgentFileSkill skill,
        AgentFileSkillScript script,
        JsonElement? arguments,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(script.FullPath))
        {
            return $"Error: Script file not found: {script.FullPath}";
        }

        var extension = Path.GetExtension(script.FullPath);
        var interpreter = extension switch
        {
            ".py" => "python3",
            ".js" => "node",
            ".sh" => "bash",
            ".ps1" => "pwsh",
            ".cs" => "dotnet",
            _ => null,
        };

        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(script.FullPath) ?? ".",
        };

        if (interpreter is not null)
        {
            startInfo.FileName = interpreter;
            startInfo.ArgumentList.Add(script.FullPath);
        }
        else
        {
            startInfo.FileName = script.FullPath;
        }

        if (arguments is {ValueKind: JsonValueKind.Array} json)
        {
            // Positional CLI arguments
            foreach (var element in json.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    return
                        $"Error: File-based skill scripts only accept string CLI arguments but received a JSON element of kind '{element.ValueKind}'. " +
                        "All array elements must be JSON strings.";
                }

                startInfo.ArgumentList.Add(element.GetString()!);
            }
        }
        //校验参数是否为数组字符串
        else if (arguments is {ValueKind: JsonValueKind.String} arrStr &&
                 AIContentJsonSerializerContext.TryDeserializeArrayString(arrStr.GetString(), out var result) &&
                 result!=null)
        {
            foreach (var item in result)
            {
                startInfo.ArgumentList.Add(item);
            }
        }
        else if (arguments is not null && arguments.Value.ValueKind != JsonValueKind.Null &&
                 arguments.Value.ValueKind != JsonValueKind.Undefined)
        {
            return
                $"Error: Expected a JSON array of CLI arguments but received {arguments.Value.ValueKind}. " +
                "File-based skill scripts expect positional arguments as a JSON array of strings.";
        }

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                return $"Error: Failed to start process for script '{script.Name}'.";
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            string output = await outputTask.ConfigureAwait(false);
            string error = await errorTask.ConfigureAwait(false);

            if (!string.IsNullOrEmpty(error))
            {
                output += $"\nStderr:\n{error}";
            }

            if (process.ExitCode != 0)
            {
                output += $"\nScript exited with code {process.ExitCode}";
            }

            return string.IsNullOrEmpty(output) ? "(no output)" : output.Trim();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Kill the process on cancellation to avoid leaving orphaned subprocesses.
            process?.Kill(entireProcessTree: true);
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Error: Failed to execute script '{script.Name}': {ex.Message}";
        }
        finally
        {
            process?.Dispose();
        }
    }
}