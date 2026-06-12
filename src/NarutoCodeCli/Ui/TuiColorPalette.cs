namespace NarutoCodeCli.Ui;

/// <summary>
/// 定义 TUI 渲染使用的一组语义化颜色。
/// </summary>
internal sealed record TuiColorPalette
{
    /// <summary>
    /// 调色板名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

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
