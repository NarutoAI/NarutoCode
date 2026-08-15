using System.Text;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 聊天输入的静态辅助：工具审批判定与命令参数拆分。
/// </summary>
internal static class ChatPromptReader
{
    private const string DefaultInputHint = "⏎ send    Ctrl+Enter 换行    Ctrl+V 贴图（可多张，Enter 发送）    / commands    Esc clear    Ctrl+C cancel / exit";
    private const string SlashCommandHint = "commands: /provider [name] · /effort <low|medium|high|xhigh> · /image <path...> [text] · /pi [text] · /exit";

    /// <summary>
    /// 根据当前输入返回底部提示；仅当首字符为斜杠时展示全部支持的斜杠命令。
    /// </summary>
    /// <param name="input">输入框当前文本。</param>
    /// <returns>应展示在输入框下方的提示文本。</returns>
    public static string GetInputHint(string? input)
    {
        return input?.StartsWith('/') == true ? SlashCommandHint : DefaultInputHint;
    }

    /// <summary>
    /// 将输入文本中的换行符统一为 \n（CRLF 与 CR 均归一为 LF），供多行消息提交前做归一化处理。
    /// </summary>
    /// <param name="text">原始输入文本。</param>
    /// <returns>归一化后的文本；空输入返回空字符串。</returns>
    public static string NormalizeLineEndings(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    /// <summary>
    /// 判断输入是否为合法工具审批结果。
    /// </summary>
    /// <param name="input">用户输入。</param>
    /// <returns>合法时返回 <see langword="true" />。</returns>
    public static bool IsToolApprovalResponse(string input)
    {
        var normalizedInput = input.Trim();
        return normalizedInput is "1" or "0";
    }

    /// <summary>
    /// 将用户输入拆分为命令参数，支持使用双引号包裹包含空格的路径。
    /// </summary>
    /// <param name="input">用户输入。</param>
    /// <returns>拆分后的参数集合。</returns>
    public static IReadOnlyList<string> SplitArguments(string input)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var character in input)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }
}
