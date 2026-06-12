namespace NarutoCodeCli.Ui;

/// <summary>
/// NarutoCode TUI 预置配色方案。
/// </summary>
internal static class TuiColorPalettes
{
    /// <summary>
    /// 薄荷青橙方案，清新轻快，适合作为默认青春风格。
    /// </summary>
    public static readonly TuiColorPalette MintCitrus = new()
    {
        Name = "Mint Citrus",
        Ink = "#243238",
        Muted = "#60717A",
        Subtle = "#B7C5CA",
        Accent = "#0E9F6E",
        AccentStrong = "#0891B2",
        Secondary = "#FF9F1C",
        Thinking = "#2DD4BF",
        Warning = "#FF9F1C",
        Danger = "#EF476F"
    };

    /// <summary>
    /// 校园清新方案，绿与牛仔蓝组合，活力但更耐看。
    /// </summary>
    public static readonly TuiColorPalette FreshCampus = new()
    {
        Name = "Fresh Campus",
        Ink = "#1E293B",
        Muted = "#64748B",
        Subtle = "#A7B0C0",
        Accent = "#10B981",
        AccentStrong = "#0F766E",
        Secondary = "#2563EB",
        Thinking = "#60A5FA",
        Warning = "#F59E0B",
        Danger = "#F43F5E"
    };

    /// <summary>
    /// 汽水泡泡方案，蓝绿与莓果粉组合，更跳跃、更年轻。
    /// </summary>
    public static readonly TuiColorPalette SodaPop = new()
    {
        Name = "Soda Pop",
        Ink = "#1F2937",
        Muted = "#6B7280",
        Subtle = "#A7B0C0",
        Accent = "#06B6D4",
        AccentStrong = "#0891B2",
        Secondary = "#F43F5E",
        Thinking = "#06B6D4",
        Warning = "#F97316",
        Danger = "#E11D48"
    };

    /// <summary>
    /// 当前默认使用的青春轻量配色方案。
    /// </summary>
    public static TuiColorPalette Current => MintCitrus;
}
