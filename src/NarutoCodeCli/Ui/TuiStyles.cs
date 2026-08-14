using Terminal.Gui.Drawing;

namespace NarutoCodeCli.Ui;

/// <summary>
/// Terminal.Gui 的深色紫罗兰工作台配色。所有 Scheme 使用显式背景，避免受宿主终端浅色主题和默认焦点反色影响。
/// 配色参考 opencode 的深紫/靛蓝基调：强调色用于焦点、输入、品牌与关键状态，正文使用亮冷白保证对比度。
/// 真彩色终端直接使用 24 位色值；仅支持 256 色的终端（如 macOS Terminal.app）回退到 256 色索引对应的
/// 标准色值，避免驱动将任意 RGB 量化为灰暗的近似色，保证任何终端下都保持鲜艳。
/// </summary>
internal static class TuiStyles
{
    // 深紫黑基底：亮紫罗兰作为品牌与交互主色，翠绿/琥珀/亮红承担状态语义。
    private static readonly Color Canvas = new("#0B0B0F");
    private static readonly Color Surface = new("#14141A");
    private static readonly Color InputSurface = new("#1C1C24");
    private static readonly Color InputFocusSurface = new("#25252F");
    private static readonly Color Ink = new("#ECECF1");
    private static readonly Color Muted = new("#9B9BA8");
    private static readonly Color Subtle = new("#6A6A76");
    private static readonly Color Accent = new("#8B5CF6");
    private static readonly Color AccentStrong = new("#C4B5FD");
    private static readonly Color Secondary = new("#A78BFA");
    private static readonly Color Thinking = new("#7C7C8C");
    private static readonly Color Warning = new("#F59E0B");
    private static readonly Color Danger = new("#F87171");
    private static readonly Color Success = new("#34D399");
    private static readonly Color Brand = new("#A78BFA");

    // 256 色友好色板：色值取自 xterm 256 色标准索引，保证仅 256 色终端下量化误差≈0 且保持鲜艳。
    // 索引参考：99 #875FFF 亮紫蓝、141 #AF87FF 亮紫、214 #FFAF00 亮橙、203 #FF5F5F 亮红、42 #00D787 翠绿。
    private static readonly Color Canvas256 = new("#080808");
    private static readonly Color Surface256 = new("#121212");
    private static readonly Color InputSurface256 = new("#1C1C1C");
    private static readonly Color InputFocusSurface256 = new("#262626");
    private static readonly Color Ink256 = new("#EEEEEE");
    private static readonly Color Muted256 = new("#878787");
    private static readonly Color Subtle256 = new("#5F5F5F");
    private static readonly Color Accent256 = new("#875FFF");
    private static readonly Color AccentStrong256 = new("#AF87FF");
    private static readonly Color Secondary256 = new("#AF87FF");
    private static readonly Color Thinking256 = new("#5F5F87");
    private static readonly Color Warning256 = new("#FFAF00");
    private static readonly Color Danger256 = new("#FF5F5F");
    private static readonly Color Success256 = new("#00D787");
    private static readonly Color Brand256 = new("#AF87FF");

    private static readonly Scheme CanvasScheme;
    private static readonly Scheme InputScheme;
    private static readonly Scheme InputPromptScheme;
    private static readonly Scheme InputPanelScheme;
    private static readonly Dictionary<UiTextStyle, Scheme> Schemes;
    private static readonly Scheme BrandScheme;
    private static readonly Scheme DividerScheme;
    private static readonly Scheme RunningScheme;
    private static readonly Scheme ReadyScheme;

    /// <summary>
    /// 静态构造：先探测终端真彩色能力，再按能力选择对应色板初始化全部 Scheme。
    /// </summary>
    static TuiStyles()
    {
        var trueColor = UseTrueColor();

        // 按终端能力选择颜色（256 色终端使用标准索引色，量化后仍保持鲜艳）
        var canvas = trueColor ? Canvas : Canvas256;
        var surface = trueColor ? Surface : Surface256;
        var inputSurface = trueColor ? InputSurface : InputSurface256;
        var inputFocusSurface = trueColor ? InputFocusSurface : InputFocusSurface256;
        var ink = trueColor ? Ink : Ink256;
        var muted = trueColor ? Muted : Muted256;
        var subtle = trueColor ? Subtle : Subtle256;
        var accent = trueColor ? Accent : Accent256;
        var accentStrong = trueColor ? AccentStrong : AccentStrong256;
        var secondary = trueColor ? Secondary : Secondary256;
        var thinking = trueColor ? Thinking : Thinking256;
        var warning = trueColor ? Warning : Warning256;
        var danger = trueColor ? Danger : Danger256;
        var success = trueColor ? Success : Success256;
        var brand = trueColor ? Brand : Brand256;

        CanvasScheme = Create(ink, canvas);
        InputScheme = new Scheme(new Terminal.Gui.Drawing.Attribute(ink, inputSurface))
        {
            Focus = new Terminal.Gui.Drawing.Attribute(ink, inputFocusSurface),
            Active = new Terminal.Gui.Drawing.Attribute(ink, inputFocusSurface, TextStyle.Bold),
            Editable = new Terminal.Gui.Drawing.Attribute(ink, inputFocusSurface),
            Highlight = new Terminal.Gui.Drawing.Attribute(ink, inputFocusSurface)
        };
        InputPromptScheme = Create(accentStrong, inputSurface, TextStyle.Bold);
        InputPanelScheme = Create(muted, inputSurface);

        Schemes = new Dictionary<UiTextStyle, Scheme>
        {
            [UiTextStyle.Normal] = Create(ink, canvas),
            [UiTextStyle.Muted] = Create(muted, canvas),
            [UiTextStyle.Subtle] = Create(subtle, canvas),
            [UiTextStyle.Accent] = Create(accent, canvas, TextStyle.Bold),
            [UiTextStyle.AccentStrong] = Create(accentStrong, canvas, TextStyle.Bold),
            [UiTextStyle.Secondary] = Create(secondary, canvas),
            [UiTextStyle.Thinking] = Create(thinking, canvas),
            [UiTextStyle.Warning] = Create(warning, canvas, TextStyle.Bold),
            [UiTextStyle.Danger] = Create(danger, canvas, TextStyle.Bold),
            [UiTextStyle.Code] = Create(accentStrong, surface)
        };

        BrandScheme = Create(brand, canvas, TextStyle.Bold);
        DividerScheme = Create(subtle, canvas);
        RunningScheme = Create(accent, canvas);
        ReadyScheme = Create(success, canvas);
    }

    /// <summary>
    /// 探测当前终端是否支持 24 位真彩色。
    /// Terminal.app 仅支持 256 色（TERM_PROGRAM=Apple_Terminal），iTerm2/WezTerm/VS Code/Warp 等支持真彩色。
    /// </summary>
    /// <returns>支持真彩色返回 <see langword="true" />。</returns>
    private static bool UseTrueColor()
    {
        // iTerm2、VS Code、WezTerm 等主流终端会设置 TERM_PROGRAM；Apple_Terminal 不支持 truecolor 需排除
        var termProgram = Environment.GetEnvironmentVariable("TERM_PROGRAM");
        if (termProgram is "iTerm.app" or "WezTerm" or "vscode" or "WarpTerminal" or "ghostty")
        {
            return true;
        }

        // COLORTERM=truecolor/24bit 是终端显式声明支持真彩色的标准方式
        var colorTerm = Environment.GetEnvironmentVariable("COLORTERM");
        if (!string.IsNullOrEmpty(colorTerm)
            && (colorTerm.Contains("truecolor", StringComparison.OrdinalIgnoreCase)
                || colorTerm.Contains("24bit", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // TERM 形如 xterm-direct、alacritty-direct 时也视为支持真彩色
        var term = Environment.GetEnvironmentVariable("TERM");
        return !string.IsNullOrEmpty(term)
               && (term.Contains("direct", StringComparison.OrdinalIgnoreCase)
                   || term.Contains("24bit", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 获取用于填充顶层窗口的画布 Scheme。
    /// </summary>
    /// <returns>深色画布 Scheme。</returns>
    public static Scheme GetCanvasScheme() => CanvasScheme;

    /// <summary>
    /// 获取聊天输入框的显式状态 Scheme，避免焦点状态反色。
    /// </summary>
    /// <returns>输入框 Scheme。</returns>
    public static Scheme GetInputScheme() => InputScheme;

    /// <summary>
    /// 获取输入提示符的 Scheme。
    /// </summary>
    /// <returns>输入提示符 Scheme。</returns>
    public static Scheme GetInputPromptScheme() => InputPromptScheme;

    /// <summary>
    /// 获取输入面板的辅助信息 Scheme。
    /// </summary>
    /// <returns>输入面板辅助信息 Scheme。</returns>
    public static Scheme GetInputPanelScheme() => InputPanelScheme;

    /// <summary>
    /// 获取指定文本样式对应的 Scheme。
    /// </summary>
    /// <param name="style">文本样式。</param>
    /// <returns>可赋给 Label 的 Scheme。</returns>
    public static Scheme GetScheme(UiTextStyle style)
    {
        return Schemes.TryGetValue(style, out var scheme) ? scheme : Schemes[UiTextStyle.Normal];
    }

    /// <summary>
    /// 获取品牌标题（会话名/产品名）的 Scheme。
    /// </summary>
    /// <returns>品牌标题 Scheme。</returns>
    public static Scheme GetBrandScheme() => BrandScheme;

    /// <summary>
    /// 获取分隔线的 Scheme。
    /// </summary>
    /// <returns>分隔线 Scheme。</returns>
    public static Scheme GetDividerScheme() => DividerScheme;

    /// <summary>
    /// 获取运行中状态文本的 Scheme。
    /// </summary>
    /// <returns>运行中状态 Scheme。</returns>
    public static Scheme GetRunningScheme() => RunningScheme;

    /// <summary>
    /// 获取就绪状态文本的 Scheme。
    /// </summary>
    /// <returns>就绪状态 Scheme。</returns>
    public static Scheme GetReadyScheme() => ReadyScheme;

    private static Scheme Create(Color foreground, Color background, TextStyle textStyle = TextStyle.None)
    {
        return new Scheme(new Terminal.Gui.Drawing.Attribute(foreground, background, textStyle));
    }
}
