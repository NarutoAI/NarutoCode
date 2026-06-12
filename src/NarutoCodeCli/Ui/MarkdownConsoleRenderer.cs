using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace NarutoCodeCli.Ui;

internal static partial class MarkdownConsoleRenderer
{
    private const string CodeFenceMarker = "```";
    private const int MaxCachedMarkdownLength = 32 * 1024;
    private const int MaxRenderCacheCount = 128;
    private static readonly TuiColorPalette Palette = TuiColorPalettes.Current;

    private static readonly object RenderCacheLock = new();
    private static readonly Dictionary<RenderCacheKey, IRenderable[]> RenderCache = [];
    private static readonly Queue<RenderCacheKey> RenderCacheOrder = [];

    private static readonly string[] KnownCodeLanguages =
    [
        "typescript",
        "javascript",
        "powershell",
        "plaintext",
        "markdown",
        "python",
        "mermaid",
        "csharp",
        "json",
        "bash",
        "yaml",
        "html",
        "text",
        "xml",
        "css",
        "sql",
        "txt",
        "yml",
        "sh",
        "ts",
        "js",
        "cs",
        "md"
    ];

    public static IReadOnlyList<IRenderable> Render(string markdown, string linePrefix = "", bool cacheResult = true)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return [new Markup($"{linePrefix}[bold {Palette.Accent}]⏳ waiting for response...[/]")];
        }

        if (!cacheResult || !CanCache(markdown))
        {
            return RenderCore(markdown, linePrefix).ToArray();
        }

        var cacheKey = new RenderCacheKey(markdown, linePrefix);
        lock (RenderCacheLock)
        {
            if (RenderCache.TryGetValue(cacheKey, out var cachedRenderables))
            {
                return cachedRenderables;
            }
        }

        var renderables = RenderCore(markdown, linePrefix).ToArray();
        lock (RenderCacheLock)
        {
            if (RenderCache.TryGetValue(cacheKey, out var cachedRenderables))
            {
                return cachedRenderables;
            }

            while (RenderCache.Count >= MaxRenderCacheCount && RenderCacheOrder.TryDequeue(out var removedKey))
            {
                RenderCache.Remove(removedKey);
            }

            RenderCache[cacheKey] = renderables;
            RenderCacheOrder.Enqueue(cacheKey);
        }

        return renderables;
    }

    private static IEnumerable<IRenderable> RenderCore(string markdown, string linePrefix)
    {
        var normalizedMarkdown = NormalizeLineBreaks(markdown);
        var lines = normalizedMarkdown.Split('\n');
        var codeLines = new List<string>();
        var inCodeBlock = false;
        var codeLanguage = string.Empty;

        foreach (var rawLine in lines)
        {
            var line = rawLine;

            while (true)
            {
                if (inCodeBlock)
                {
                    var closeIndex = line.IndexOf(CodeFenceMarker, StringComparison.Ordinal);
                    if (closeIndex < 0)
                    {
                        codeLines.Add(line);
                        break;
                    }

                    var codeLine = line[..closeIndex];
                    if (codeLine.Length > 0)
                    {
                        codeLines.Add(codeLine);
                    }

                    yield return CreateCodeBlock(codeLines, codeLanguage, linePrefix);
                    codeLines.Clear();
                    codeLanguage = string.Empty;
                    inCodeBlock = false;

                    line = line[(closeIndex + CodeFenceMarker.Length)..].TrimStart();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        break;
                    }

                    continue;
                }

                var openIndex = line.IndexOf(CodeFenceMarker, StringComparison.Ordinal);
                if (openIndex < 0)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        yield return new Markup(linePrefix);
                    }
                    else
                    {
                        yield return RenderLine(line, linePrefix);
                    }

                    break;
                }

                var fenceBodyAndRest = line[(openIndex + CodeFenceMarker.Length)..];
                var inlineCloseIndex = fenceBodyAndRest.IndexOf(CodeFenceMarker, StringComparison.Ordinal);
                if (inlineCloseIndex >= 0)
                {
                    var inlineFenceBody = fenceBodyAndRest[..inlineCloseIndex];
                    if (ShouldRenderAsInlineCode(inlineFenceBody))
                    {
                        var beforeInlineCode = line[..openIndex];
                        var afterInlineCode = fenceBodyAndRest[(inlineCloseIndex + CodeFenceMarker.Length)..];
                        line = $"{beforeInlineCode}`{inlineFenceBody.Trim()}`{afterInlineCode}";
                        continue;
                    }

                    var beforeInlineFence = line[..openIndex].TrimEnd();
                    if (!string.IsNullOrWhiteSpace(beforeInlineFence))
                    {
                        yield return RenderLine(beforeInlineFence, linePrefix);
                    }

                    var (language, firstCodeLine) = ParseInlineCodeFence(inlineFenceBody);
                    var inlineCodeLines = new List<string>();
                    if (!string.IsNullOrEmpty(firstCodeLine))
                    {
                        inlineCodeLines.Add(firstCodeLine);
                    }

                    yield return CreateCodeBlock(inlineCodeLines, language, linePrefix);

                    line = fenceBodyAndRest[(inlineCloseIndex + CodeFenceMarker.Length)..].TrimStart();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        break;
                    }

                    continue;
                }

                var beforeFence = line[..openIndex].TrimEnd();
                if (!string.IsNullOrWhiteSpace(beforeFence))
                {
                    yield return RenderLine(beforeFence, linePrefix);
                }

                var opening = ParseCodeFenceOpening(fenceBodyAndRest);
                codeLanguage = opening.Language;
                inCodeBlock = true;

                if (!string.IsNullOrEmpty(opening.FirstCodeLine))
                {
                    codeLines.Add(opening.FirstCodeLine);
                }

                break;
            }
        }

        if (inCodeBlock)
        {
            yield return CreateCodeBlock(codeLines, codeLanguage, linePrefix);
        }
    }

    private static bool CanCache(string markdown)
    {
        return markdown.Length <= MaxCachedMarkdownLength;
    }

    private static string NormalizeLineBreaks(string markdown)
    {
        return markdown
            .Replace(@"\\r\\n", "\n", StringComparison.Ordinal)
            .Replace(@"\\n", "\n", StringComparison.Ordinal)
            .Replace(@"\\r", "\n", StringComparison.Ordinal)
            .Replace(@"\r\n", "\n", StringComparison.Ordinal)
            .Replace(@"\n", "\n", StringComparison.Ordinal)
            .Replace(@"\r", "\n", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static (string Language, string? FirstCodeLine) ParseCodeFenceOpening(string fenceInfo)
    {
        var normalizedFenceInfo = fenceInfo.TrimStart();
        if (string.IsNullOrEmpty(normalizedFenceInfo))
        {
            return (string.Empty, null);
        }

        foreach (var language in KnownCodeLanguages)
        {
            if (!normalizedFenceInfo.StartsWith(language, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var firstCodeLine = normalizedFenceInfo[language.Length..].TrimStart();
            return (language, string.IsNullOrEmpty(firstCodeLine) ? null : firstCodeLine);
        }

        var whitespaceIndex = normalizedFenceInfo.IndexOfAny([' ', '\t']);
        if (whitespaceIndex > 0)
        {
            var language = normalizedFenceInfo[..whitespaceIndex].Trim();
            var firstCodeLine = normalizedFenceInfo[(whitespaceIndex + 1)..].TrimStart();
            return (language, string.IsNullOrEmpty(firstCodeLine) ? null : firstCodeLine);
        }

        return (normalizedFenceInfo, null);
    }

    private static (string Language, string? FirstCodeLine) ParseInlineCodeFence(string fenceInfo)
    {
        var opening = ParseCodeFenceOpening(fenceInfo);
        if (!string.IsNullOrEmpty(opening.FirstCodeLine) || IsKnownCodeLanguage(opening.Language))
        {
            return opening;
        }

        var codeLine = fenceInfo.Trim();
        return string.IsNullOrEmpty(codeLine) ? (string.Empty, null) : (string.Empty, codeLine);
    }

    private static bool ShouldRenderAsInlineCode(string fenceInfo)
    {
        var opening = ParseCodeFenceOpening(fenceInfo);
        return string.IsNullOrEmpty(opening.FirstCodeLine) && !IsKnownCodeLanguage(opening.Language);
    }

    private static bool IsKnownCodeLanguage(string language)
    {
        return KnownCodeLanguages.Any(knownLanguage => string.Equals(knownLanguage, language, StringComparison.OrdinalIgnoreCase));
    }

    private static IRenderable RenderLine(string line, string linePrefix)
    {
        var trimmed = line.TrimStart();

        if (trimmed.StartsWith("### ", StringComparison.Ordinal))
        {
            return new Markup($"{linePrefix}[bold {Palette.Accent}]{RenderInline(trimmed[4..])}[/]");
        }

        if (trimmed.StartsWith("## ", StringComparison.Ordinal))
        {
            return new Markup($"{linePrefix}[bold {Palette.Accent}]{RenderInline(trimmed[3..])}[/]");
        }

        if (trimmed.StartsWith("# ", StringComparison.Ordinal))
        {
            return new Markup($"{linePrefix}[bold {Palette.Accent}]{RenderInline(trimmed[2..])}[/]");
        }

        if (trimmed.StartsWith("- ", StringComparison.Ordinal))
        {
            return new Markup($"{linePrefix}[{Palette.Subtle}] • [/]{RenderInline(trimmed[2..])}");
        }

        if (trimmed.StartsWith("> ", StringComparison.Ordinal))
        {
            return new Markup($"{linePrefix}[italic {Palette.Muted}]{RenderInline(trimmed[2..])}[/]");
        }

        return new Markup($"{linePrefix}[{Palette.Ink}]{RenderInline(line)}[/]");
    }

    private static IRenderable CreateCodeBlock(IReadOnlyCollection<string> lines, string language, string linePrefix)
    {
        var title = string.IsNullOrWhiteSpace(language) ? "code" : language;
        var languageStyle = GetCodeLanguageStyle(title);
        var rows = new List<IRenderable>
        {
            new Markup($"{linePrefix}[{Palette.Muted}]code[/] [{Palette.Subtle}]·[/] [{Palette.Muted}]{Markup.Escape(title)}[/]")
        };

        foreach (var line in lines)
        {
            rows.Add(new Markup($"{linePrefix}  [{languageStyle}]{Markup.Escape(line)}[/]"));
        }

        return new Rows(rows);
    }

    private static bool IsCSharpLanguage(string language)
    {
        return language.Equals("csharp", StringComparison.OrdinalIgnoreCase)
               || language.Equals("cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCodeLanguageStyle(string language)
    {
        if (IsCSharpLanguage(language))
        {
            return Palette.Secondary;
        }

        return language.ToLowerInvariant() switch
        {
            "json" => Palette.Secondary,
            "bash" or "sh" or "powershell" => Palette.AccentStrong,
            "yaml" or "yml" => Palette.Warning,
            "markdown" or "md" => Palette.Secondary,
            "html" or "xml" => Palette.Danger,
            "css" => Palette.Secondary,
            "text" or "txt" or "plaintext" => Palette.Ink,
            _ => Palette.Ink
        };
    }

    private static string RenderInline(string text)
    {
        var escaped = Markup.Escape(text);
        escaped = InlineCodeRegex().Replace(escaped, $"[bold {Palette.Secondary}]$1[/]");
        escaped = BoldRegex().Replace(escaped, "[bold]$1[/]");
        escaped = ItalicRegex().Replace(escaped, "[italic]$1[/]");
        return escaped;
    }

    private readonly record struct RenderCacheKey(string Markdown, string LinePrefix);

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex("\\*\\*([^*]+)\\*\\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex("\\*([^*]+)\\*")]
    private static partial Regex ItalicRegex();
}
