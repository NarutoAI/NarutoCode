namespace NarutoCodeCli.Ui;

/// <summary>
/// TUI 渲染行的语义化样式，用于将消息内容映射为终端颜色。
/// </summary>
internal enum ChatRenderStyle
{
    /// <summary>
    /// 正文颜色。
    /// </summary>
    Normal,

    /// <summary>
    /// 弱化文本。
    /// </summary>
    Muted,

    /// <summary>
    /// 更弱的分隔与辅助文本。
    /// </summary>
    Subtle,

    /// <summary>
    /// 主强调色（标题、用户角色、状态）。
    /// </summary>
    Accent,

    /// <summary>
    /// 次级强调色（助手角色、代码、工具调用）。
    /// </summary>
    Secondary,

    /// <summary>
    /// 思考过程颜色。
    /// </summary>
    Thinking,

    /// <summary>
    /// 警告颜色。
    /// </summary>
    Warning,

    /// <summary>
    /// 错误颜色。
    /// </summary>
    Danger
}

/// <summary>
/// 一条待渲染的终端文本行。
/// </summary>
/// <param name="Text">行的文本内容，不包含颜色标记。</param>
/// <param name="Style">行的语义化样式。</param>
internal readonly record struct ChatRenderLine(string Text, ChatRenderStyle Style);
