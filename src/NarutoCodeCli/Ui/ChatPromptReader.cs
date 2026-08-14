using System.Text;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 聊天输入的静态辅助：工具审批判定与命令参数拆分。
/// </summary>
internal static class ChatPromptReader
{
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
