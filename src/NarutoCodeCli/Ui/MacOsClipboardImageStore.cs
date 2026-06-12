using System.Diagnostics;
using NarutoCode.Domain.Workspaces;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 从 macOS 剪贴板读取图片并保存到当前工作区临时目录。
/// </summary>
internal sealed class MacOsClipboardImageStore(IWorkspaceContextAccessor workspaceContextAccessor) : IClipboardImageStore
{
    private static readonly string ClipboardImageDirectory = Path.Combine("tmp", "clipboard-images");
    
    public bool TrySaveClipboardImages(out IReadOnlyList<string> relativePaths)
    {
        relativePaths = [];

        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var workspacePath = workspaceContextAccessor.Current.WorkingDirectory;
        var imageDirectory = Path.Combine(workspacePath, ClipboardImageDirectory);
        Directory.CreateDirectory(imageDirectory);

        if (TrySaveClipboardImageData(imageDirectory, out var imagePath))
        {
            relativePaths = [imagePath];
            return true;
        }

        return false;
    }

    private static bool TrySaveClipboardImageData(string imageDirectory, out string relativePath)
    {
        var fileName = $"clipboard-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.png";
        var outputPath = Path.Combine(imageDirectory, fileName);
        if (!TryWriteClipboardData("PNGf", outputPath, out _))
        {
            var tiffPath = Path.ChangeExtension(outputPath, ".tiff");
            if (!TryWriteClipboardData("TIFF", tiffPath, out _))
            {
                TryDeleteFile(tiffPath);
                relativePath = string.Empty;
                return false;
            }

            if (!TryConvertTiffToPng(tiffPath, outputPath, out _))
            {
                TryDeleteFile(tiffPath);
                relativePath = string.Empty;
                return false;
            }

            TryDeleteFile(tiffPath);
        }

        relativePath = ToRelativeClipboardPath(outputPath);
        return true;
    }

    private static bool TryWriteClipboardData(string appleScriptClass, string outputPath, out string error)
    {
        var scriptPath = EscapeAppleScriptString(outputPath);
        var result = RunProcess(
            "/usr/bin/osascript",
            [
                "-e", $"set outputFile to POSIX file \"{scriptPath}\"",
                "-e", "set fileHandle to missing value",
                "-e", "try",
                "-e", $"set imageData to the clipboard as «class {appleScriptClass}»",
                "-e", "set fileHandle to open for access outputFile with write permission",
                "-e", "set eof fileHandle to 0",
                "-e", "write imageData to fileHandle",
                "-e", "close access fileHandle",
                "-e", "on error errMsg",
                "-e", "if fileHandle is not missing value then close access fileHandle",
                "-e", "error errMsg",
                "-e", "end try"
            ]);

        error = result.Error;
        return result.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
    }

    private static bool TryConvertTiffToPng(string tiffPath, string outputPath, out string error)
    {
        var result = RunProcess("/usr/bin/sips", ["-s", "format", "png", tiffPath, "--out", outputPath]);
        error = result.Error;
        return result.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
    }

    private static (int ExitCode, string Output, string Error) RunProcess(string fileName, IReadOnlyList<string> arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.UseShellExecute = false;
        process.Start();
        process.WaitForExit();
        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd().Trim();
        return (process.ExitCode, output, string.IsNullOrWhiteSpace(error) ? output : error);
    }

    private static string ToRelativeClipboardPath(string outputPath)
    {
        return Path.Combine(ClipboardImageDirectory, Path.GetFileName(outputPath)).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string EscapeAppleScriptString(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (IOException)
        {
            // 临时文件删除失败不应阻塞图片发送流程。
        }
        catch (UnauthorizedAccessException)
        {
            // 临时文件删除失败不应阻塞图片发送流程。
        }
    }
}
