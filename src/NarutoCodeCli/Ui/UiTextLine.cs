namespace NarutoCodeCli.Ui;

/// <summary>
/// 消息视图中一行文本的样式类别。
/// </summary>
internal enum UiTextStyle
{
    /// <summary>普通正文。</summary>
    Normal,

    /// <summary>弱化文本。</summary>
    Muted,

    /// <summary>更弱的边框和分隔文本。</summary>
    Subtle,

    /// <summary>主强调（用户角色标记、标题）。</summary>
    Accent,

    /// <summary>强主强调。</summary>
    AccentStrong,

    /// <summary>次级强调（助手角色标记、工具）。</summary>
    Secondary,

    /// <summary>思考过程。</summary>
    Thinking,

    /// <summary>警告。</summary>
    Warning,

    /// <summary>错误。</summary>
    Danger,

    /// <summary>代码内容。</summary>
    Code
}

/// <summary>
/// 带样式的单行文本。
/// </summary>
/// <param name="Style">文本样式。</param>
/// <param name="Text">文本内容（不含 ANSI 转义）。</param>
internal readonly record struct StyledLine(UiTextStyle Style, string Text);
