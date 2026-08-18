using System.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 聊天多行输入框：基于 TextView，支持多行粘贴与多行编辑。
/// Enter 提交输入；Ctrl+Enter / Alt+Enter 插入换行；Esc 清空草稿并复位光标；
/// 粘贴保留完整多行文本；Ctrl+V 优先尝试图片粘贴；
/// Ctrl+C 有选中文字时放行给复制命令，无选中时转发为取消请求。
/// </summary>
internal sealed class ChatInputField : TextView
{
    /// <summary>
    /// 用户按下 Enter 时触发（提交输入，不插入换行）。
    /// </summary>
    public event Action? SubmitPressed;

    /// <summary>
    /// 用户按下 Ctrl+V 且剪贴板含图片时触发；回调返回 <see langword="true" /> 表示按键已被消费，
    /// 返回 <see langword="false" /> 表示剪贴板无图片，放行默认多行文本粘贴。
    /// </summary>
    public event Func<bool>? PasteImageRequested;

    /// <summary>
    /// 用户按下 Ctrl+C 且输入框无选中文字时触发（取消当前任务 / 退出）。
    /// 有选中文字时不触发，按键放行给 TextView 的复制命令。
    /// </summary>
    public event Action? CancelRequested;

    /// <summary>
    /// 创建多行聊天输入框：默认开启多行编辑与自动折行。
    /// WordWrap 依赖 Multiline，必须先开启 Multiline；折行宽度按 Viewport 在布局时计算。
    /// </summary>
    public ChatInputField()
    {
        Multiline = true;
        WordWrap = true;
    }

    private bool wrapReady;

    /// <inheritdoc />
    /// <remarks>
    /// 首次布局完成、Viewport 宽度就绪后强制重建折行模型：
    /// 构造函数中 WordWrap=true 时 Viewport.Width 为 0，折行宽度会被污染，
    /// 这里重新应用 WordWrap 让 _frameWidth 使用真实宽度（见 2.4.17 WordWrapManager）。
    /// </remarks>
    protected override void OnSubViewsLaidOut(LayoutEventArgs e)
    {
        base.OnSubViewsLaidOut(e);

        if (wrapReady)
        {
            return;
        }

        wrapReady = true;
        // 先关再开：WordWrap setter 在值未变化时直接返回，toggle 强制走 WrapTextModel 重建
        WordWrap = false;
        WordWrap = true;
    }

    /// <inheritdoc />
    protected override bool OnKeyDown(Key key)
    {
        // Enter 提交输入：消费按键，避免 TextView 默认将其作为换行插入
        if (key == Key.Enter)
        {
            SubmitPressed?.Invoke();
            return true;
        }

        // Ctrl+Enter / Alt+Enter 插入换行：多数终端把 Ctrl+Enter 报告为带 Ctrl 修饰的 Enter
        if ((key.KeyCode & KeyCode.CharMask) == KeyCode.Enter && (key.IsCtrl || key.IsAlt))
        {
            InvokeCommand(Command.NewLine);
            return true;
        }

        // Esc 优先关闭覆盖层（Autocomplete 候选框 / 右键菜单 popover）：
        // 这些层盖在输入框上，若在此直接消费 Esc，覆盖层将无法关闭，
        // 后续所有键盘与鼠标输入都会被其吞掉，表现为"界面无法操作"。
        // 仅在没有覆盖层需要关闭时才执行"清空草稿并复位光标"。
        if (key == Key.Esc)
        {
            // Autocomplete 的 CloseKey 默认就是 Esc：主动交给它处理并检查返回值，
            // 只有它确实消费了 Esc（候选框关闭）才返回，避免 Esc 冒泡到框架的 Quit 命令。
            if (Autocomplete.Suggestions.Count > 0 && Autocomplete.ProcessKey(key))
            {
                return true;
            }

            // 存在 Popover 覆盖层（如右键菜单）时放行给框架：Popover 把 Esc 绑定到 Command.Quit（隐藏自身）。
            if (App?.Popovers?.GetActivePopover() is { Visible: true })
            {
                return base.OnKeyDown(key);
            }

            Text = string.Empty;
            InvokeCommand(Command.Start);
            return true;
        }

        // Ctrl+C：有选中文字时放行给 TextView 的复制命令；无选中时转发取消请求并消费按键，
        // 避免触发 TextView "无选中复制当前行" 的默认行为污染剪贴板
        if (key.IsCtrl && (key.KeyCode & KeyCode.CharMask) == KeyCode.C)
        {
            if (SelectedLength > 0)
            {
                return false;
            }

            CancelRequested?.Invoke();
            return true;
        }

        // Ctrl+V 优先尝试图片粘贴：剪贴板含图片时消费按键，否则交给基类走多行文本粘贴
        if (key.IsCtrl && (key.KeyCode & KeyCode.CharMask) == KeyCode.V)
        {
            if (PasteImageRequested?.Invoke() == true)
            {
                return true;
            }
        }

        return base.OnKeyDown(key);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 聊天输入框不需要右键菜单；同时在鼠标释放/点击时防御性解除鼠标 grab，
    /// 避免 Released 事件丢失后残留 grab，导致整个界面无法点击。
    /// </remarks>
    protected override bool OnMouseEvent(Mouse mouse)
    {
        // 拦截右键点击和 Ctrl+左键释放：Terminal.Gui 默认把这两者绑定到 Command.Context（弹出右键菜单），
        // 聊天输入框的右键菜单没有实际用途，弹出后还会吞掉后续键盘与鼠标输入。
        if (mouse.Flags.HasFlag(MouseFlags.RightButtonClicked)
            || (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && mouse.Flags.HasFlag(MouseFlags.Ctrl)))
        {
            return true;
        }

        // 释放/点击时防御性解除鼠标 grab：当鼠标在终端窗口外松开、或事件被中文输入法等抢占时，
        // TextView 的 Released 处理可能丢失，导致 grab 残留——此后所有点击都会被路由到输入框，
        // 表现为"无法点击"。
        if (mouse.IsReleased || mouse.IsSingleDoubleOrTripleClicked)
        {
            App?.Mouse.UngrabMouse();
        }

        var handled = base.OnMouseEvent(mouse);

        // Terminal.Gui 2.4.17 的 TextView 对宽字符已计算显示列宽，但只在宽度大于 2 时
        // 才按字符中点修正插入位置。中文等两个终端列宽字符不会进入该分支，点击右半格仍会
        // 落在字符前。普通单击完成基类的焦点和鼠标状态处理后，按实际终端列宽重新定位。
        if (handled && mouse.Flags == MouseFlags.LeftButtonClicked && mouse.Position is { } position)
        {
            SetInsertionPointFromDisplayColumn(position);
        }

        return handled;
    }

    /// <summary>
    /// 根据鼠标在当前视口中的显示列设置插入点，确保双列宽字符的右半格落在字符之后。
    /// </summary>
    /// <param name="position">相对输入框视口的鼠标位置。</param>
    private void SetInsertionPointFromDisplayColumn(Point position)
    {
        var row = Math.Clamp(Viewport.Y + position.Y, 0, Math.Max(0, Lines - 1));
        var line = GetLine(row);
        var targetColumn = Math.Max(0, Viewport.X + position.X);
        var displayColumn = 0;
        var insertionPoint = 0;

        foreach (var cell in line)
        {
            // 使用 TextView 公开的列宽计算，保持 Tab 与组合字符的框架语义。
            var width = Math.Max(1, GetColumnsWidth([cell]));
            if (targetColumn < displayColumn + width)
            {
                // 点击宽字符的右半格时，把光标放到该字符之后；左半格则放在之前。
                if (targetColumn - displayColumn >= (width + 1) / 2)
                {
                    insertionPoint++;
                }

                break;
            }

            displayColumn += width;
            insertionPoint++;
        }

        InsertionPoint = new Point(insertionPoint, row);
        SetNeedsDraw();
    }
}
