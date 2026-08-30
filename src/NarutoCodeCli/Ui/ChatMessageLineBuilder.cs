using System.Text;
using System.Text.RegularExpressions;
using NarutoCode.Domain.Messages;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 将聊天消息转换为带样式的文本行：负责 Markdown 子集解析（标题、列表、引用、代码块）、
/// 行内标记清理（行内代码/加粗/斜体只保留文本）和按终端列宽折行。
/// </summary>
internal static partial class ChatMessageLineBuilder
{
    private const string CodeFenceMarker = "```";
    private const string Indent = "    ";
    private const string EventIndent = "      ";

    /// <summary>
    /// 构建一条消息的全部显示行。
    /// </summary>
    /// <param name="message">聊天消息视图模型。</param>
    /// <param name="contentWidth">消息区可用列宽。</param>
    /// <returns>带样式的文本行集合。</returns>
    public static IReadOnlyList<StyledLine> Build(ChatMessage message, int contentWidth)
    {
        var width = Math.Max(16, contentWidth);
        var lines = new List<StyledLine>();

        if (message.Role == ChatRole.User)
        {
            // 用户消息以角色标记开头，与助手输出形成视觉区分。
            lines.Add(new StyledLine(UiTextStyle.AccentStrong, Indent + "❯ you"));
            AppendMarkdownLines(lines, message.Content, UiTextStyle.Normal, Indent, width);
        }
        else
        {
            if (message.AgentMessages.Count == 0)
            {
                lines.Add(new StyledLine(UiTextStyle.Muted, Indent + "working..."));
            }
            else
            {
                lines.Add(new StyledLine(UiTextStyle.Secondary, Indent + "✦ assistant"));
                foreach (var agentMessage in message.AgentMessages)
                {
                    if (agentMessage.Type == AgentMessageType.Usage)
                    {
                        continue;
                    }

                    AppendAgentMessage(lines, agentMessage, width);
                }
            }
        }

        // 消息之间的分隔空行
        lines.Add(new StyledLine(UiTextStyle.Subtle, string.Empty));
        return lines;
    }

    /// <summary>
    /// 追加一条 Agent 分段消息的显示行（tool/审批/错误/普通内容）。
    /// Thinking 分段不再渲染到消息区，避免长推理占据屏幕；思考中状态由 ChatTuiWindow 状态栏指示。
    /// </summary>
    private static void AppendAgentMessage(List<StyledLine> lines, AgentMessage agentMessage, int width)
    {
        switch (agentMessage.Type)
        {
            case AgentMessageType.Thinking:
                // 故意不渲染思考正文，避免长推理占据屏幕。
                break;

            case AgentMessageType.ToolCall:
                lines.Add(new StyledLine(UiTextStyle.Secondary, Indent + "⚙ tool"));
                AppendMarkdownLines(lines, agentMessage.Content, UiTextStyle.Muted, EventIndent, width - 2);
                break;

            case AgentMessageType.ToolApprovalRequest:
                lines.Add(new StyledLine(UiTextStyle.Warning, Indent + "⚠ approval required"));
                AppendMarkdownLines(lines, agentMessage.Content, UiTextStyle.Normal, EventIndent, width - 2);
                lines.Add(new StyledLine(UiTextStyle.Muted, Indent + "输入 1 同意，输入 0 拒绝"));
                break;

            case AgentMessageType.Error:
                lines.Add(new StyledLine(UiTextStyle.Danger, Indent + "✕ error"));
                AppendMarkdownLines(lines, agentMessage.Content, UiTextStyle.Danger, EventIndent, width - 2);
                break;

            default:
                AppendMarkdownLines(lines, agentMessage.Content, UiTextStyle.Normal, Indent, width);
                break;
        }
    }

    /// <summary>
    /// 解析 Markdown 行（标题/列表/引用/代码块），清理行内标记并按宽度折行追加。
    /// </summary>
    private static void AppendMarkdownLines(
        List<StyledLine> lines,
        string markdown,
        UiTextStyle baseStyle,
        string indent,
        int width)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        var normalized = markdown
            .Replace(@"\r\n", "\n", StringComparison.Ordinal)
            .Replace(@"\n", "\n", StringComparison.Ordinal)
            .Replace(@"\r", "\n", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var codeLines = new List<string>();
        var inCodeBlock = false;
        var codeLanguage = string.Empty;

        foreach (var rawLine in normalized.Split('\n'))
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

                    AppendCodeBlock(lines, codeLines, codeLanguage, indent, width);
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
                    AppendPlainLine(lines, line, baseStyle, indent, width);
                    break;
                }

                var fenceBodyAndRest = line[(openIndex + CodeFenceMarker.Length)..];
                var inlineCloseIndex = fenceBodyAndRest.IndexOf(CodeFenceMarker, StringComparison.Ordinal);
                if (inlineCloseIndex >= 0)
                {
                    // 行内围栏：非已知语言时按行内代码处理，只保留文本
                    var inlineFenceBody = fenceBodyAndRest[..inlineCloseIndex];
                    var before = line[..openIndex].TrimEnd();
                    var after = fenceBodyAndRest[(inlineCloseIndex + CodeFenceMarker.Length)..].TrimStart();
                    var merged = $"{before}`{inlineFenceBody.Trim()}`{after}".Trim();
                    AppendPlainLine(lines, merged, baseStyle, indent, width);
                    break;
                }

                var beforeFence = line[..openIndex].TrimEnd();
                if (!string.IsNullOrWhiteSpace(beforeFence))
                {
                    AppendPlainLine(lines, beforeFence, baseStyle, indent, width);
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
            AppendCodeBlock(lines, codeLines, codeLanguage, indent, width);
        }
    }

    /// <summary>
    /// 追加一行普通文本（识别标题/列表/引用后按宽度折行）。
    /// </summary>
    private static void AppendPlainLine(List<StyledLine> lines, string line, UiTextStyle baseStyle, string indent, int width)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            lines.Add(new StyledLine(UiTextStyle.Subtle, indent.TrimEnd()));
            return;
        }

        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("### ", StringComparison.Ordinal))
        {
            AppendWrapped(lines, StripInlineMarkers(trimmed[4..]), UiTextStyle.Accent, indent, width);
            return;
        }

        if (trimmed.StartsWith("## ", StringComparison.Ordinal))
        {
            AppendWrapped(lines, StripInlineMarkers(trimmed[3..]), UiTextStyle.Accent, indent, width);
            return;
        }

        if (trimmed.StartsWith("# ", StringComparison.Ordinal))
        {
            AppendWrapped(lines, StripInlineMarkers(trimmed[2..]), UiTextStyle.Accent, indent, width);
            return;
        }

        if (trimmed.StartsWith("- ", StringComparison.Ordinal))
        {
            var text = StripInlineMarkers(trimmed[2..]);
            AppendWrapped(lines, $"• {text}", baseStyle, indent, width);
            return;
        }

        if (trimmed.StartsWith("> ", StringComparison.Ordinal))
        {
            AppendWrapped(lines, StripInlineMarkers(trimmed[2..]), UiTextStyle.Muted, indent, width);
            return;
        }

        AppendWrapped(lines, StripInlineMarkers(line), baseStyle, indent, width);
    }

    /// <summary>
    /// 追加一个代码块（语言标题行 + 代码行）。
    /// </summary>
    private static void AppendCodeBlock(List<StyledLine> lines, IReadOnlyCollection<string> codeLines, string language, string indent, int width)
    {
        var title = string.IsNullOrWhiteSpace(language) ? "code" : language;
        lines.Add(new StyledLine(UiTextStyle.Muted, $"{indent}▸ {title}"));
        foreach (var codeLine in codeLines)
        {
            AppendWrapped(lines, codeLine, UiTextStyle.Code, indent, width);
        }
    }

    /// <summary>
    /// 按列宽折行追加文本（识别可折行单词，超长 token 按字符硬切）。
    /// </summary>
    private static void AppendWrapped(List<StyledLine> lines, string text, UiTextStyle style, string indent, int width)
    {
        var available = Math.Max(1, width);
        var indentWidth = TextWidth(indent);
        var buffer = new StringBuilder(indent);
        var bufferWidth = indentWidth;

        foreach (var token in text.Split(' '))
        {
            if (token.Length == 0)
            {
                continue;
            }

            var tokenWidth = TextWidth(token);
            var separatorWidth = bufferWidth > indentWidth ? 1 : 0;
            if (bufferWidth + separatorWidth + tokenWidth > available && bufferWidth > indentWidth)
            {
                lines.Add(new StyledLine(style, buffer.ToString()));
                buffer.Clear();
                buffer.Append(indent);
                bufferWidth = indentWidth;
            }

            if (bufferWidth > indentWidth)
            {
                buffer.Append(' ');
                bufferWidth++;
            }

            // 单个 token 超出可用宽度时按字符硬切
            if (indentWidth + tokenWidth > available)
            {
                buffer.Append(token);
                bufferWidth += tokenWidth;
                FlushHardWrapped(lines, buffer, style, indent, indentWidth, available);
            }
            else
            {
                buffer.Append(token);
                bufferWidth += tokenWidth;
            }
        }

        if (buffer.Length > indentWidth)
        {
            lines.Add(new StyledLine(style, buffer.ToString()));
        }
    }

    /// <summary>
    /// 将已超宽的行按字符切分为多行输出。
    /// </summary>
    private static void FlushHardWrapped(
        List<StyledLine> lines,
        StringBuilder buffer,
        UiTextStyle style,
        string indent,
        int indentWidth,
        int available)
    {
        while (buffer.Length > 0)
        {
            var content = buffer.ToString(indentWidth, buffer.Length - indentWidth);
            var contentWidth = TextWidth(content);
            if (contentWidth <= available - indentWidth || buffer.Length <= indentWidth)
            {
                lines.Add(new StyledLine(style, buffer.ToString()));
                buffer.Clear();
                return;
            }

            // 逐字符找出放得下的前缀
            var prefixWidth = 0;
            var splitIndex = -1;
            foreach (var (index, rune) in content.EnumerateRunes().Index())
            {
                var runeWidth = RuneWidth(rune);
                if (indentWidth + prefixWidth + runeWidth > available)
                {
                    break;
                }

                prefixWidth += runeWidth;
                splitIndex = index + 1;
            }

            if (splitIndex <= 0)
            {
                // 极端情况：一个字符都放不下时强制放一个字符，避免死循环
                splitIndex = 1;
                prefixWidth = RuneWidth(content.EnumerateRunes().First());
            }

            var prefix = content[..splitIndex];
            lines.Add(new StyledLine(style, indent + prefix));
            buffer.Clear();
            buffer.Append(indent);
            buffer.Append(content[splitIndex..]);
        }
    }

    /// <summary>
    /// 清理行内标记（行内代码、加粗、斜体），只保留文本内容。
    /// </summary>
    private static string StripInlineMarkers(string text)
    {
        var result = InlineCodeRegex().Replace(text, "$1");
        result = BoldRegex().Replace(result, "$1");
        result = ItalicRegex().Replace(result, "$1");
        return result;
    }

    private static (string Language, string? FirstCodeLine) ParseCodeFenceOpening(string fenceInfo)
    {
        var normalized = fenceInfo.TrimStart();
        if (string.IsNullOrEmpty(normalized))
        {
            return (string.Empty, null);
        }

        // 语言名 + 首个代码行（围栏后紧跟内容时）
        var whitespaceIndex = normalized.IndexOfAny([' ', '\t']);
        if (whitespaceIndex > 0)
        {
            var language = normalized[..whitespaceIndex].Trim();
            var firstCodeLine = normalized[(whitespaceIndex + 1)..].TrimStart();
            return (language, string.IsNullOrEmpty(firstCodeLine) ? null : firstCodeLine);
        }

        return (normalized, null);
    }

    /// <summary>
    /// 计算字符串在终端中的显示列宽（东亚全角字符按 2 列）。
    /// </summary>
    private static int TextWidth(string text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += RuneWidth(rune);
        }

        return width;
    }

    private static int RuneWidth(Rune rune)
    {
        if (rune.IsAscii)
        {
            return 1;
        }

        var value = rune.Value;
        // CJK 与东亚全角字符范围按 2 列计算
        if ((value >= 0x1100 && value <= 0x115F)
            || (value >= 0x2E80 && value <= 0xA4CF)
            || (value >= 0xAC00 && value <= 0xD7A3)
            || (value >= 0xF900 && value <= 0xFAFF)
            || (value >= 0xFE30 && value <= 0xFE4F)
            || (value >= 0xFF00 && value <= 0xFF60)
            || (value >= 0xFFE0 && value <= 0xFFE6)
            || (value >= 0x20000 && value <= 0x2FFFD)
            || (value >= 0x30000 && value <= 0x3FFFD))
        {
            return 2;
        }

        return 1;
    }

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex("\\*\\*([^*]+)\\*\\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex("\\*([^*]+)\\*")]
    private static partial Regex ItalicRegex();
}
