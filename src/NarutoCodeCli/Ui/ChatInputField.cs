using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
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

        // Esc 清空草稿并复位光标，消费按键避免触发框架默认 Cancel 行为导致焦点失效；
        // 待发送图片由窗口单独管理，不在此处清除，用户仍可继续输入说明后发送。
        // CurrentRow/CurrentColumn 在 2.4.17 为只读，光标复位走框架 Start 命令。
        if (key == Key.Esc)
        {
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
}
