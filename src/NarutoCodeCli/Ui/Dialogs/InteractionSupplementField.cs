using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 选择问卷的补充说明多行输入框：Enter 提交，Esc/Ctrl+C 交由交互弹窗处理。
/// </summary>
internal sealed class InteractionSupplementField : TextView
{
    /// <summary>
    /// 用户按下 Enter 时触发，用于提交当前问卷。
    /// </summary>
    public event Action? SubmitPressed;

    /// <summary>
    /// 用户按下 Tab（前移）/Shift+Tab（回退）时触发，参数为 <see langword="true" /> 表示前移；
    /// 由宿主问卷决定是切换题目还是进出补充说明模式。
    /// </summary>
    public event Action<bool>? TabRequested;

    /// <summary>
    /// 用户请求取消当前交互时触发；参数表示是否同时请求取消正在运行的 Agent 任务。
    /// </summary>
    public event Action<bool>? CancelRequested;

    /// <summary>
    /// 选项区导航按键回调：返回 <see langword="true" /> 表示按键已被选项区消费（移动高亮、切换多选勾选）。
    /// 输入框获得焦点时方向键/空格仍操控选项，避免被文本编辑吞掉；单选场景下空格照常输入。
    /// </summary>
    public Func<Key, bool>? NavigationKeyHandler { get; set; }

    /// <summary>
    /// 消息区滚动按键回调：PgUp/PgDn/Home/End 转发给主窗口消息区，
    /// 输入框聚焦时用户仍可翻看历史输出；返回 <see langword="true" /> 表示已消费。
    /// </summary>
    public Func<Key, bool>? ScrollKeyHandler { get; set; }

    /// <summary>
    /// 创建补充说明输入框，默认启用多行编辑与自动折行。
    /// </summary>
    public InteractionSupplementField()
    {
        Multiline = true;
        WordWrap = true;
    }

    /// <inheritdoc />
    protected override bool OnKeyDown(Key key)
    {
        // Enter 直接提交，避免补充说明框与选择区出现不一致的确认方式。
        if ((key.KeyCode & KeyCode.CharMask) == KeyCode.Enter)
        {
            SubmitPressed?.Invoke();
            return true;
        }

        if (key == Key.Esc)
        {
            CancelRequested?.Invoke(false);
            return true;
        }

        if (key.IsCtrl && (key.KeyCode & KeyCode.CharMask) == KeyCode.C)
        {
            CancelRequested?.Invoke(true);
            return true;
        }

        // 问卷内的焦点循环：说明框的 Tab/Shift+Tab 交回宿主问卷处理（切题或退出补充模式）。
        if (key == Key.Tab)
        {
            TabRequested?.Invoke(true);
            return true;
        }

        if (key == Key.Tab.WithShift)
        {
            TabRequested?.Invoke(false);
            return true;
        }

        // 消息区滚动键优先转发：补充说明聚焦时 PgUp/PgDn/Home/End 不移动光标，而是翻看历史消息。
        if ((key == Key.PageUp || key == Key.PageDown || key == Key.Home || key == Key.End)
            && ScrollKeyHandler?.Invoke(key) == true)
        {
            return true;
        }

        // 选项区优先：说明框聚焦时方向键/空格仍操控问卷，只有宿主未消费的按键才进入文本编辑。
        var isSpace = (key.KeyCode & KeyCode.CharMask) == KeyCode.Space;
        if ((key == Key.CursorUp || key == Key.CursorDown || isSpace)
            && NavigationKeyHandler?.Invoke(key) == true)
        {
            return true;
        }

        return base.OnKeyDown(key);
    }
}
