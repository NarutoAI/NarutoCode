using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace NarutoCode.Infrastructure.Tools;

/// <summary>
/// 提供给 Agent 调用的文件搜索工具集合。
/// </summary>
public static class SearchAgentTools
{
    /// <summary>
    /// 默认最大返回结果数量。
    /// </summary>
    private const int DefaultMaxResults = 100;

    /// <summary>
    /// 默认最大输出字节数，避免搜索结果撑爆模型上下文。
    /// </summary>
    private const int DefaultMaxOutputBytes = 64 * 1024;

    /// <summary>
    /// 默认最大单行输出长度。
    /// </summary>
    private const int DefaultMaxLineLength = 160;

    /// <summary>
    /// 默认最大递归深度。
    /// </summary>
    private const int DefaultMaxDepth = 25;

    /// <summary>
    /// 单个搜索文件最大大小，超过后跳过。
    /// </summary>
    private const long MaxSearchFileBytes = 10 * 1024 * 1024;

    /// <summary>
    /// 默认忽略的大目录，避免搜索过程扫爆项目。
    /// </summary>
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".idea",
        ".vs",
        ".vscode",
        "bin",
        "obj",
        "node_modules",
        "dist",
        "build",
        "out",
        ".next",
        "coverage",
        "target",
        ".venv"
    };

    /// <summary>
    /// 按 Glob 通配符模式搜索文件和目录，并按最后修改时间倒序返回。
    /// </summary>
    /// <param name="pattern">Glob 模式，例如 *.cs、**/*.json、src/**/*.cs。</param>
    /// <param name="path">搜索根目录；为空时使用当前工作目录。</param>
    /// <param name="hidden">是否包含隐藏文件和隐藏目录。</param>
    /// <param name="followSymlinks">是否跟随符号链接目录。</param>
    /// <param name="maxDepth">最大递归深度。</param>
    /// <param name="maxResults">最大返回结果数量。</param>
    /// <returns>每行一个匹配路径；无匹配时返回提示文本。</returns>
    [Description("按 Glob 通配符模式搜索文件和目录，例如 *.cs、**/*.json、src/**/*.cs，并按最后修改时间倒序返回")]
    public static Task<string> Glob(
        [Description("Glob 模式，例如 *.cs、**/*.json、src/**/*.cs")]
        string pattern,
        [Description("搜索根目录；为空时使用当前工作目录")]
        string? path = null,
        [Description("是否包含隐藏文件和隐藏目录")]
        bool hidden = false,
        [Description("是否跟随符号链接目录")]
        bool followSymlinks = false,
        [Description("最大递归深度，默认 25")]
        int maxDepth = DefaultMaxDepth,
        [Description("最大返回结果数量，默认 100")]
        int maxResults = DefaultMaxResults)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Task.FromResult("Glob 模式不能为空。");
        }

        var searchRoot = ResolveSearchRoot(path);
        if (!Directory.Exists(searchRoot))
        {
            return Task.FromResult($"搜索目录不存在: {searchRoot}");
        }

        var normalizedPattern = NormalizePath(pattern.Trim());
        var matcher = CreateGlobRegex(normalizedPattern);
        var hasDirectorySegment = normalizedPattern.Contains('/');
        var cappedDepth = Math.Clamp(maxDepth, 0, DefaultMaxDepth);
        var cappedResults = Math.Clamp(maxResults, 1, DefaultMaxResults);
        var rootInfo = new DirectoryInfo(searchRoot);
        var matches = new List<SearchPathMatch>();

        foreach (var entry in EnumerateEntries(rootInfo, cappedDepth, hidden, followSymlinks))
        {
            var relativePath = NormalizePath(Path.GetRelativePath(searchRoot, entry.FullName));
            var target = hasDirectorySegment ? relativePath : entry.Name;
            if (!matcher.IsMatch(target))
            {
                continue;
            }

            matches.Add(new SearchPathMatch(entry.FullName, entry.LastWriteTimeUtc));
        }

        var limitedMatches = matches
            .OrderByDescending(static item => item.LastWriteTimeUtc)
            .ThenBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Take(cappedResults)
            .Select(static item => item.Path)
            .ToArray();

        if (limitedMatches.Length == 0)
        {
            return Task.FromResult("未找到匹配的文件或目录。");
        }

        var result = new StringBuilder();
        foreach (var item in limitedMatches)
        {
            result.AppendLine(item);
        }

        if (matches.Count > limitedMatches.Length)
        {
            result.AppendLine($"[结果已截断，仅显示前 {limitedMatches.Length} 条，共匹配 {matches.Count} 条]");
        }

        return Task.FromResult(result.ToString().TrimEnd());
    }

    /// <summary>
    /// 使用正则表达式搜索文件内容，并返回 file:line:text 格式的匹配结果。
    /// </summary>
    /// <param name="pattern">要搜索的正则表达式；literal 为 true 时按普通文本匹配。</param>
    /// <param name="path">搜索文件或目录；为空时使用当前工作目录。</param>
    /// <param name="glob">文件过滤 Glob 模式，例如 *.cs、**/*.md。</param>
    /// <param name="caseSensitive">是否大小写敏感。</param>
    /// <param name="literal">是否将 pattern 当作普通文本而不是正则表达式。</param>
    /// <param name="hidden">是否包含隐藏文件和隐藏目录。</param>
    /// <param name="maxDepth">目录递归最大深度。</param>
    /// <param name="maxResults">最大匹配结果数量。</param>
    /// <param name="maxOutputBytes">最大输出字节数。</param>
    /// <param name="maxLineLength">单行最大输出长度。</param>
    /// <param name="beforeContext">每个匹配前输出的上下文行数。</param>
    /// <param name="afterContext">每个匹配后输出的上下文行数。</param>
    /// <returns>匹配结果文本；无匹配时返回提示文本。</returns>
    [Description("使用正则表达式搜索文件内容，返回 file:line:text 格式结果；支持 Glob 文件过滤、大小写控制和上下文行")]
    public static async Task<string> Grep(
        [Description("要搜索的正则表达式；literal 为 true 时按普通文本匹配")]
        string pattern,
        [Description("搜索文件或目录；为空时使用当前工作目录")]
        string? path = null,
        [Description("文件过滤 Glob 模式，例如 *.cs、**/*.md，默认 * 表示所有文件")]
        string glob = "*",
        [Description("是否大小写敏感，默认 false")]
        bool caseSensitive = false,
        [Description("是否将 pattern 当作普通文本而不是正则表达式")]
        bool literal = false,
        [Description("是否包含隐藏文件和隐藏目录")]
        bool hidden = false,
        [Description("目录递归最大深度，默认 25")]
        int maxDepth = DefaultMaxDepth,
        [Description("最大匹配结果数量，默认 100")]
        int maxResults = DefaultMaxResults,
        [Description("最大输出字节数，默认 65536")]
        int maxOutputBytes = DefaultMaxOutputBytes,
        [Description("单行最大输出长度，默认 160")]
        int maxLineLength = DefaultMaxLineLength,
        [Description("每个匹配前输出的上下文行数")]
        int beforeContext = 0,
        [Description("每个匹配后输出的上下文行数")]
        int afterContext = 0)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return "搜索正则表达式不能为空。";
        }

        var searchRoot = ResolveSearchRoot(path);
        var searchFiles = GetSearchFiles(searchRoot, glob, hidden, followSymlinks: false, Math.Clamp(maxDepth, 0, DefaultMaxDepth));
        var regexPattern = literal ? Regex.Escape(pattern) : pattern;
        var regexOptions = RegexOptions.CultureInvariant | RegexOptions.Compiled;
        if (!caseSensitive)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        Regex regex;
        try
        {
            regex = new Regex(regexPattern, regexOptions, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException exception)
        {
            return $"无效的正则表达式: {exception.Message}";
        }

        var context = new GrepOutputContext(
            Math.Clamp(maxResults, 1, DefaultMaxResults),
            Math.Clamp(maxOutputBytes, 1024, DefaultMaxOutputBytes),
            Math.Clamp(maxLineLength, 40, 1000),
            Math.Clamp(beforeContext, 0, 20),
            Math.Clamp(afterContext, 0, 20));

        foreach (var filePath in searchFiles)
        {
            if (context.ShouldStop)
            {
                break;
            }

            await SearchFileAsync(filePath, searchRoot, regex, context);
        }

        if (context.MatchCount == 0)
        {
            return "未找到匹配内容。";
        }

        return context.BuildResult();
    }

    /// <summary>
    /// 获取搜索根目录；未传入时使用当前工作目录。
    /// </summary>
    /// <param name="path">用户传入的路径。</param>
    /// <returns>绝对搜索路径。</returns>
    private static string ResolveSearchRoot(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(path.Trim());
    }

    /// <summary>
    /// 根据搜索路径获取待搜索文件列表。
    /// </summary>
    /// <param name="searchRoot">搜索文件或目录。</param>
    /// <param name="glob">文件过滤 Glob 模式。</param>
    /// <param name="hidden">是否包含隐藏文件。</param>
    /// <param name="followSymlinks">是否跟随符号链接。</param>
    /// <param name="maxDepth">最大递归深度。</param>
    /// <returns>待搜索文件路径序列。</returns>
    private static IEnumerable<string> GetSearchFiles(
        string searchRoot,
        string glob,
        bool hidden,
        bool followSymlinks,
        int maxDepth)
    {
        if (File.Exists(searchRoot))
        {
            yield return searchRoot;
            yield break;
        }

        if (!Directory.Exists(searchRoot))
        {
            yield break;
        }

        var normalizedPattern = NormalizePath(string.IsNullOrWhiteSpace(glob) ? "*" : glob.Trim());
        var matcher = CreateGlobRegex(normalizedPattern);
        var hasDirectorySegment = normalizedPattern.Contains('/');
        var rootInfo = new DirectoryInfo(searchRoot);

        foreach (var entry in EnumerateEntries(rootInfo, maxDepth, hidden, followSymlinks))
        {
            if (entry is not FileInfo fileInfo)
            {
                continue;
            }

            var relativePath = NormalizePath(Path.GetRelativePath(searchRoot, fileInfo.FullName));
            var target = hasDirectorySegment ? relativePath : fileInfo.Name;
            if (matcher.IsMatch(target))
            {
                yield return fileInfo.FullName;
            }
        }
    }

    /// <summary>
    /// 枚举目录中的文件系统项，并执行隐藏项、忽略目录和符号链接过滤。
    /// </summary>
    /// <param name="root">根目录。</param>
    /// <param name="maxDepth">最大递归深度。</param>
    /// <param name="includeHidden">是否包含隐藏项。</param>
    /// <param name="followSymlinks">是否跟随符号链接目录。</param>
    /// <returns>文件系统项序列。</returns>
    private static IEnumerable<FileSystemInfo> EnumerateEntries(
        DirectoryInfo root,
        int maxDepth,
        bool includeHidden,
        bool followSymlinks)
    {
        var stack = new Stack<(DirectoryInfo Directory, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            var (directory, depth) = stack.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = directory.GetFileSystemInfos();
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (!includeHidden && IsHidden(entry))
                {
                    continue;
                }

                if (entry is DirectoryInfo childDirectory)
                {
                    if (IgnoredDirectoryNames.Contains(childDirectory.Name))
                    {
                        continue;
                    }

                    var isSymlink = childDirectory.Attributes.HasFlag(FileAttributes.ReparsePoint);
                    if (isSymlink && !followSymlinks)
                    {
                        continue;
                    }

                    yield return childDirectory;
                    if (depth < maxDepth)
                    {
                        stack.Push((childDirectory, depth + 1));
                    }

                    continue;
                }

                yield return entry;
            }
        }
    }

    /// <summary>
    /// 搜索单个文件内容。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="searchRoot">搜索根路径。</param>
    /// <param name="regex">匹配正则。</param>
    /// <param name="context">输出上下文。</param>
    private static async Task SearchFileAsync(string filePath, string searchRoot, Regex regex, GrepOutputContext context)
    {
        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(filePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        if (!fileInfo.Exists || fileInfo.Length == 0 || fileInfo.Length > MaxSearchFileBytes || await IsBinaryFileAsync(filePath))
        {
            return;
        }

        var relativePath = NormalizePath(Path.GetRelativePath(searchRoot, filePath));
        var beforeLines = new Queue<(int LineNumber, string Text)>();
        var afterRemaining = 0;
        var lastWrittenLine = 0;
        var lineNumber = 0;

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

        while (!context.ShouldStop)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            lineNumber++;

            bool matched;
            try
            {
                matched = regex.IsMatch(line);
            }
            catch (RegexMatchTimeoutException)
            {
                context.AppendWarning($"正则匹配超时，已跳过文件: {filePath}");
                return;
            }

            if (matched)
            {
                foreach (var beforeLine in beforeLines.Where(item => item.LineNumber > lastWrittenLine))
                {
                    context.AppendLine(relativePath, beforeLine.LineNumber, beforeLine.Text, isContextLine: true);
                    lastWrittenLine = beforeLine.LineNumber;
                }

                context.AppendLine(relativePath, lineNumber, line, isContextLine: false);
                context.IncrementMatchCount();
                lastWrittenLine = lineNumber;
                afterRemaining = context.AfterContext;
            }
            else if (afterRemaining > 0 && lineNumber > lastWrittenLine)
            {
                context.AppendLine(relativePath, lineNumber, line, isContextLine: true);
                lastWrittenLine = lineNumber;
                afterRemaining--;
            }

            if (context.BeforeContext > 0)
            {
                beforeLines.Enqueue((lineNumber, line));
                while (beforeLines.Count > context.BeforeContext)
                {
                    beforeLines.Dequeue();
                }
            }
        }
    }

    /// <summary>
    /// 判断文件是否为二进制文件。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <returns>如果文件像二进制文件则返回 true。</returns>
    private static async Task<bool> IsBinaryFileAsync(string filePath)
    {
        var buffer = new byte[4096];
        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var read = await stream.ReadAsync(buffer);
            return buffer.AsSpan(0, read).Contains((byte)0);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    /// <summary>
    /// 创建 Glob 匹配正则表达式。
    /// </summary>
    /// <param name="pattern">Glob 模式。</param>
    /// <returns>用于匹配路径的正则表达式。</returns>
    private static Regex CreateGlobRegex(string pattern)
    {
        var builder = new StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            if (character == '*')
            {
                var isDoubleStar = index + 1 < pattern.Length && pattern[index + 1] == '*';
                if (isDoubleStar)
                {
                    index++;
                    if (index + 1 < pattern.Length && pattern[index + 1] == '/')
                    {
                        builder.Append("(?:.*/)?");
                        index++;
                    }
                    else
                    {
                        builder.Append(".*");
                    }
                }
                else
                {
                    builder.Append("[^/]*");
                }

                continue;
            }

            if (character == '?')
            {
                builder.Append("[^/]");
                continue;
            }

            builder.Append(character == '/' ? "/" : Regex.Escape(character.ToString()));
        }

        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// 标准化路径分隔符。
    /// </summary>
    /// <param name="path">路径文本。</param>
    /// <returns>使用 / 作为分隔符的路径。</returns>
    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    /// <summary>
    /// 判断文件系统项是否为隐藏项。
    /// </summary>
    /// <param name="entry">文件系统项。</param>
    /// <returns>隐藏项返回 true。</returns>
    private static bool IsHidden(FileSystemInfo entry)
    {
        return entry.Name.StartsWith(".", StringComparison.Ordinal) || entry.Attributes.HasFlag(FileAttributes.Hidden);
    }

    /// <summary>
    /// 搜索路径匹配项。
    /// </summary>
    /// <param name="Path">文件或目录完整路径。</param>
    /// <param name="LastWriteTimeUtc">最后修改时间。</param>
    private sealed record SearchPathMatch(string Path, DateTime LastWriteTimeUtc);

    /// <summary>
    /// Grep 输出上下文，负责结果数量、字节数和截断控制。
    /// </summary>
    private sealed class GrepOutputContext
    {
        private readonly StringBuilder output = new();
        private readonly List<string> warnings = [];
        private readonly int maxResults;
        private readonly int maxOutputBytes;
        private readonly int maxLineLength;
        private int outputBytes;

        /// <summary>
        /// 创建 Grep 输出上下文。
        /// </summary>
        public GrepOutputContext(int maxResults, int maxOutputBytes, int maxLineLength, int beforeContext, int afterContext)
        {
            this.maxResults = maxResults;
            this.maxOutputBytes = maxOutputBytes;
            this.maxLineLength = maxLineLength;
            BeforeContext = beforeContext;
            AfterContext = afterContext;
        }

        /// <summary>
        /// 匹配前上下文行数。
        /// </summary>
        public int BeforeContext { get; }

        /// <summary>
        /// 匹配后上下文行数。
        /// </summary>
        public int AfterContext { get; }

        /// <summary>
        /// 已匹配结果数量。
        /// </summary>
        public int MatchCount { get; private set; }

        /// <summary>
        /// 是否应该停止继续搜索。
        /// </summary>
        public bool ShouldStop { get; private set; }

        /// <summary>
        /// 追加一行搜索结果。
        /// </summary>
        public void AppendLine(string filePath, int lineNumber, string text, bool isContextLine)
        {
            var separator = isContextLine ? '-' : ':';
            var line = $"{filePath}{separator}{lineNumber}{separator}{TruncateLine(text)}";
            var lineBytes = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
            if (outputBytes + lineBytes > maxOutputBytes)
            {
                ShouldStop = true;
                return;
            }

            output.AppendLine(line);
            outputBytes += lineBytes;
        }

        /// <summary>
        /// 增加匹配数量。
        /// </summary>
        public void IncrementMatchCount()
        {
            MatchCount++;
            if (MatchCount >= maxResults)
            {
                ShouldStop = true;
            }
        }

        /// <summary>
        /// 追加警告信息。
        /// </summary>
        public void AppendWarning(string warning)
        {
            warnings.Add(warning);
        }

        /// <summary>
        /// 构建最终输出结果。
        /// </summary>
        public string BuildResult()
        {
            if (ShouldStop)
            {
                output.AppendLine($"[结果已截断，最多返回 {maxResults} 条匹配或 {maxOutputBytes} 字节输出]");
            }

            foreach (var warning in warnings)
            {
                output.AppendLine($"[警告] {warning}");
            }

            return output.ToString().TrimEnd();
        }

        /// <summary>
        /// 截断过长的单行文本。
        /// </summary>
        private string TruncateLine(string text)
        {
            return text.Length <= maxLineLength ? text : string.Concat(text.AsSpan(0, maxLineLength), "…");
        }
    }
}
