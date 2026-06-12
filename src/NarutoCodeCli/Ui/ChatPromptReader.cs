using System.Text;
using Spectre.Console;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 负责读取 TUI 用户输入，屏蔽交互终端和管道输入的差异。
/// </summary>
internal sealed class ChatPromptReader
{
    private static readonly TuiColorPalette Palette = TuiColorPalettes.Current;
    private static string ChatPromptMarkup => $"[{Palette.Subtle}]╰─[/] [bold {Palette.Accent}]ask[/] [{Palette.Subtle}]›[/] ";
    private static string ToolApprovalPromptMarkup => $"[{Palette.Subtle}]╰─[/] [bold {Palette.Secondary}]approve tool[/] [{Palette.Muted}](1 agree / 0 deny)[/] [{Palette.Subtle}]›[/] ";

    /// <summary>
    /// 读取一条用户输入；当输入流结束时返回 <see langword="null" />。
    /// </summary>
    /// <param name="requiresToolApproval">是否正在等待工具审批。</param>
    /// <returns>用户输入内容，或输入结束标记。</returns>
    public async Task<string?> ReadAsync(bool requiresToolApproval)
    {
        if (Console.IsInputRedirected)
        {
            var input = Console.ReadLine();
            return requiresToolApproval && input is not null && !IsToolApprovalResponse(input)
                ? null
                : input;
        }

        return requiresToolApproval ?await ReadToolApprovalAsync() :await ReadChatInputAsync();
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

    private static async Task<string> ReadChatInputAsync()
    {
        return await AnsiConsole.PromptAsync(new TextPrompt<string>(ChatPromptMarkup).PromptStyle(Palette.Ink));
    }

    private static async Task<string> ReadToolApprovalAsync()
    {
        return await AnsiConsole.PromptAsync(
            new TextPrompt<string>(ToolApprovalPromptMarkup)
                .PromptStyle(Palette.Ink)
                .Validate(input => IsToolApprovalResponse(input)
                    ? ValidationResult.Success()
                    : ValidationResult.Error($"[{Palette.Danger}]工具审批只能输入 1 同意或 0 不同意。[/]")));
    }
    
    
    #region ctrl+v 临时注释

    
    // private string ReadChatInput()
    // {
    //     AnsiConsole.Markup(ChatPromptMarkup);
    //     var input = new StringBuilder();
    //
    //     while (true)
    //     {
    //         var key = Console.ReadKey(intercept: true);
    //         if (key.Key == ConsoleKey.Enter)
    //         {
    //             Console.WriteLine();
    //             return input.ToString();
    //         }
    //
    //         if (key.Key == ConsoleKey.Backspace)
    //         {
    //             RemoveLastCharacter(input);
    //             continue;
    //         }
    //
    //         if ((key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.V) || key.KeyChar == '\u0016')
    //         {
    //             InsertClipboardImageCommand(input);
    //             continue;
    //         }
    //
    //         if (!char.IsControl(key.KeyChar))
    //         {
    //             input.Append(key.KeyChar);
    //             Console.Write(key.KeyChar);
    //         }
    //     }
    // }
    //
    //
    // private void InsertClipboardImageCommand(StringBuilder input)
    // {
    //     if (!clipboardImageStore.TrySaveClipboardImages(out var relativePaths))
    //     {
    //         return;
    //     }
    //
    //     var imageArguments = string.Join(' ', relativePaths.Select(QuoteIfRequired));
    //     var insertText = input.Length == 0
    //         ? $"/image {imageArguments} "
    //         : input.ToString().StartsWith("/image ", StringComparison.OrdinalIgnoreCase)
    //             ? $"{imageArguments} "
    //             : $" /image {imageArguments} ";
    //
    //     input.Append(insertText);
    //     Console.Write(insertText);
    // }
    //
    // private static string QuoteIfRequired(string value)
    // {
    //     return value.Any(char.IsWhiteSpace)
    //         ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
    //         : value;
    // }
    //
    // private static void RemoveLastCharacter(StringBuilder input)
    // {
    //     if (input.Length == 0)
    //     {
    //         return;
    //     }
    //
    //     input.Length--;
    //     Console.Write("\b \b");
    // }

    #endregion
}
