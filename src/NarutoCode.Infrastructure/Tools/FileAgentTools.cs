using System.ComponentModel;
using System.Text;

namespace NarutoCode.Infrastructure.Tools;

/// <summary>
/// 文件操作工具
/// </summary>
public static class FileAgentTools
{
    /// <summary>
    /// 根据文件路径读取文本文件内容。
    /// </summary>
    /// <param name="path">要读取的文件完整路径。</param>
    /// <returns>文件中的文本内容。</returns>
    [Description("根据用户输入的文件地址，读取文件内容")]
    public static async Task<string> Read([Description("文件地址")] string path)
    {
        return await File.ReadAllTextAsync(path);
    }

    /// <summary>
    /// 将文本内容写入指定文件；如果文件已存在则覆盖。
    /// </summary>
    /// <param name="path">要写入的文件完整路径。</param>
    /// <param name="content">要写入的文本内容。</param>
    /// <returns>执行结果说明。</returns>
    [Description("将内容写入指定文件，如果文件已存在则覆盖")]
    public static async Task<string> Write(
        [Description("文件地址")] string path,
        [Description("要写入的文件内容")] string content)
    {
        await File.WriteAllTextAsync(path, content);
        return "文件写入成功";
    }

    /// <summary>
    /// 编辑文件内容，将旧文本替换为新文本。
    /// </summary>
    /// <param name="path">要编辑的文件完整路径。</param>
    /// <param name="oldText">要被替换的旧文本。</param>
    /// <param name="newText">替换后的新文本。</param>
    /// <returns>执行结果说明。</returns>
    [Description("编辑文件内容，将文件中指定的旧文本替换为新文本")]
    public static async Task<string> Edit(
        [Description("文件地址")] string path,
        [Description("要被替换的旧文本")] string oldText,
        [Description("替换后的新文本")] string newText)
    {
        var content = await File.ReadAllTextAsync(path);
        if (!content.Contains(oldText))
        {
            return "未找到要替换的文本内容";
        }

        content = content.Replace(oldText, newText);
        await File.WriteAllTextAsync(path, content);
        return "文件编辑成功";
    }

    /// <summary>
    /// 向文件末尾追加文本内容；如果文件不存在则自动创建。
    /// </summary>
    /// <param name="path">目标文件完整路径。</param>
    /// <param name="content">要追加的文本内容。</param>
    /// <returns>执行结果说明。</returns>
    [Description("向指定文件末尾追加内容，如果文件不存在则自动创建")]
    public static async Task<string> Append(
        [Description("文件地址")] string path,
        [Description("要追加的文件内容")] string content)
    {
        await File.AppendAllTextAsync(path, content);
        return "文件追加成功";
    }

    /// <summary>
    /// 检查文件或目录是否存在。
    /// </summary>
    /// <param name="path">文件或目录路径。</param>
    /// <returns>存在性说明。</returns>
    [Description("检查指定文件或目录是否存在")]
    public static Task<string> Exists([Description("文件或目录地址")] string path)
    {
        var exists = File.Exists(path) || Directory.Exists(path);
        return Task.FromResult(exists ? "存在" : "不存在");
    }

    /// <summary>
    /// 创建目录；如果目录已存在则直接返回成功。
    /// </summary>
    /// <param name="path">目录路径。</param>
    /// <returns>执行结果说明。</returns>
    [Description("创建指定目录，如果目录已存在则直接返回成功")]
    public static Task<string> CreateDirectory([Description("目录地址")] string path)
    {
        Directory.CreateDirectory(path);
        return Task.FromResult("目录创建成功");
    }

    /// <summary>
    /// 复制文件到新位置。
    /// </summary>
    /// <param name="sourcePath">源文件路径。</param>
    /// <param name="destinationPath">目标文件路径。</param>
    /// <param name="overwrite">目标文件存在时是否覆盖。</param>
    /// <returns>执行结果说明。</returns>
    [Description("复制文件到目标位置，可选择是否覆盖已存在文件")]
    public static Task<string> Copy(
        [Description("源文件地址")] string sourcePath,
        [Description("目标文件地址")] string destinationPath,
        [Description("目标文件已存在时是否覆盖，true 表示覆盖")]
        bool overwrite = true)
    {
        File.Copy(sourcePath, destinationPath, overwrite);
        return Task.FromResult("文件复制成功");
    }

    /// <summary>
    /// 移动文件到新位置。
    /// </summary>
    /// <param name="sourcePath">源文件路径。</param>
    /// <param name="destinationPath">目标文件路径。</param>
    /// <param name="overwrite">目标文件存在时是否覆盖。</param>
    /// <returns>执行结果说明。</returns>
    [Description("移动文件到目标位置，可选择是否覆盖已存在文件")]
    public static Task<string> Move(
        [Description("源文件地址")] string sourcePath,
        [Description("目标文件地址")] string destinationPath,
        [Description("目标文件已存在时是否覆盖，true 表示覆盖")]
        bool overwrite = true)
    {
        File.Move(sourcePath, destinationPath, overwrite);
        return Task.FromResult("文件移动成功");
    }

    /// <summary>
    /// 删除指定文件或目录。
    /// </summary>
    /// <param name="path">文件或目录路径。</param>
    /// <param name="recursive">删除目录时是否递归删除子项。</param>
    /// <returns>执行结果说明。</returns>
    [Description("删除指定文件或目录，删除目录时可选择递归删除")]
    public static Task<string> Delete(
        [Description("文件或目录地址")] string path,
        [Description("删除目录时是否递归删除子项，true 表示递归删除")]
        bool recursive = true)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
            return Task.FromResult("文件删除成功");
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive);
            return Task.FromResult("目录删除成功");
        }

        return Task.FromResult("目标不存在，无需删除");
    }

    /// <summary>
    /// 列出目录下的文件和子目录。
    /// </summary>
    /// <param name="path">目标目录路径。</param>
    /// <param name="searchPattern">搜索模式，例如 *.cs。</param>
    /// <param name="recursive">是否递归搜索子目录。</param>
    /// <returns>目录内容列表，每行一个路径。</returns>
    [Description("列出指定目录下的文件和子目录，可按模式过滤并支持递归")]
    public static Task<string> ListFiles(
        [Description("目录地址")] string path,
        [Description("搜索模式，例如 *.cs，默认 * 表示全部")]
        string searchPattern = "*",
        [Description("是否递归搜索子目录，true 表示递归")] bool recursive = false)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var entries = Directory.GetFileSystemEntries(path, searchPattern, searchOption);
        return Task.FromResult(entries.Length == 0 ? "目录为空" : string.Join(Environment.NewLine, entries));
    }

    /// <summary>
    /// 按行读取文本文件内容。
    /// </summary>
    /// <param name="path">要读取的文件路径。</param>
    /// <returns>每行内容拼接后的结果。</returns>
    [Description("按行读取文本文件内容")]
    public static async Task<string> ReadLines([Description("文件地址")] string path)
    {
        var lines = await File.ReadAllLinesAsync(path);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 按行写入文本文件内容；如果文件存在则覆盖。
    /// </summary>
    /// <param name="path">目标文件路径。</param>
    /// <param name="lines">要写入的多行文本。</param>
    /// <returns>执行结果说明。</returns>
    [Description("按行写入文本文件内容，如果文件已存在则覆盖")]
    public static async Task<string> WriteLines(
        [Description("文件地址")] string path,
        [Description("要写入的多行文本内容，每一项代表一行")] string[] lines)
    {
        await File.WriteAllLinesAsync(path, lines);
        return "文件按行写入成功";
    }

    /// <summary>
    /// 获取文件或目录的基础信息。
    /// </summary>
    /// <param name="path">文件或目录路径。</param>
    /// <returns>格式化后的信息字符串。</returns>
    [Description("获取文件或目录的基础信息，例如名称、大小、创建时间、最后修改时间")]
    public static Task<string> GetFileInfo([Description("文件或目录地址")] string path)
    {
        if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            var result = new StringBuilder();
            result.AppendLine($"类型: 文件");
            result.AppendLine($"名称: {fileInfo.Name}");
            result.AppendLine($"完整路径: {fileInfo.FullName}");
            result.AppendLine($"大小(字节): {fileInfo.Length}");
            result.AppendLine($"创建时间: {fileInfo.CreationTime}");
            result.AppendLine($"最后修改时间: {fileInfo.LastWriteTime}");
            return Task.FromResult(result.ToString().TrimEnd());
        }

        if (Directory.Exists(path))
        {
            var directoryInfo = new DirectoryInfo(path);
            var result = new StringBuilder();
            result.AppendLine($"类型: 目录");
            result.AppendLine($"名称: {directoryInfo.Name}");
            result.AppendLine($"完整路径: {directoryInfo.FullName}");
            result.AppendLine($"创建时间: {directoryInfo.CreationTime}");
            result.AppendLine($"最后修改时间: {directoryInfo.LastWriteTime}");
            return Task.FromResult(result.ToString().TrimEnd());
        }

        return Task.FromResult("目标不存在，无法获取信息");
    }

    /// <summary>
    /// 在指定目录中搜索符合模式的文件。
    /// </summary>
    /// <param name="directoryPath">要搜索的目录路径。</param>
    /// <param name="searchPattern">搜索模式，例如 *.cs。</param>
    /// <param name="recursive">是否递归搜索子目录。</param>
    /// <returns>搜索结果列表，每行一个文件路径。</returns>
    [Description("在指定目录中搜索符合模式的文件，支持递归搜索")]
    public static Task<string> SearchFiles(
        [Description("目录地址")] string directoryPath,
        [Description("搜索模式，例如 *.cs")] string searchPattern = "*",
        [Description("是否递归搜索子目录，true 表示递归")] bool recursive = true)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(directoryPath, searchPattern, searchOption);
        return Task.FromResult(files.Length == 0 ? "未找到匹配的文件" : string.Join(Environment.NewLine, files));
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
                   AIFunctionFactory.Create(FileAgentTools.Read),
                   AIFunctionFactory.Create(FileAgentTools.Write),
                   AIFunctionFactory.Create(FileAgentTools.Edit),
                   AIFunctionFactory.Create(FileAgentTools.Append),
                   AIFunctionFactory.Create(FileAgentTools.Exists),
                   AIFunctionFactory.Create(FileAgentTools.CreateDirectory),
                   AIFunctionFactory.Create(FileAgentTools.Copy),
                   AIFunctionFactory.Create(FileAgentTools.Move),
                   AIFunctionFactory.Create(FileAgentTools.Delete),
                   AIFunctionFactory.Create(FileAgentTools.ListFiles),
                   AIFunctionFactory.Create(FileAgentTools.ReadLines),
                   AIFunctionFactory.Create(FileAgentTools.WriteLines),
                   AIFunctionFactory.Create(FileAgentTools.GetFileInfo),
                   AIFunctionFactory.Create(FileAgentTools.SearchFiles)
               };
               """;
    }
}