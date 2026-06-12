namespace NarutoCodeCli.Ui;

/// <summary>
/// 定义 TUI 渲染使用的 Spectre Console 标记颜色。
/// </summary>
internal sealed record TuiColorPalette
{
    /// <summary>
    /// 主文本颜色。
    /// </summary>
    public string Ink { get; init; } = "grey93";

    /// <summary>
    /// 弱化文本颜色。
    /// </summary>
    public string Muted { get; init; } = "grey58";

    /// <summary>
    /// 更弱的边框和分隔符颜色。
    /// </summary>
    public string Subtle { get; init; } = "grey35";

    /// <summary>
    /// 主强调色。
    /// </summary>
    public string Accent { get; init; } = "deepskyblue1";

    /// <summary>
    /// 强主强调色。
    /// </summary>
    public string AccentStrong { get; init; } = "dodgerblue1";

    /// <summary>
    /// 次级强调色。
    /// </summary>
    public string Secondary { get; init; } = "mediumpurple1";

    /// <summary>
    /// 思考状态颜色。
    /// </summary>
    public string Thinking { get; init; } = "plum1";

    /// <summary>
    /// 警告颜色。
    /// </summary>
    public string Warning { get; init; } = "yellow1";

    /// <summary>
    /// 错误和危险操作颜色。
    /// </summary>
    public string Danger { get; init; } = "red1";
}

/// <summary>
/// 提供当前 TUI 颜色主题。
/// </summary>
internal static class TuiColorPalettes
{
    /// <summary>
    /// 当前默认颜色主题。
    /// </summary>
    public static TuiColorPalette Current { get; } = new()
    {
        Ink = "#172033",
        Muted = "#667085",
        Subtle = "#B8C4B4",
        Accent = "#84CC16",
        AccentStrong = "#0891B2",
        Secondary = "#0EA5E9",
        Thinking = "#22D3EE",
        Warning = "#F59E0B",
        Danger = "#F43F5E"
    };
}
