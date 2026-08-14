using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 聊天输入框：Enter 键触发提交事件，Esc 清除当前文本草稿，避免依赖框架默认命令绑定；
/// Ctrl+V 优先尝试图片粘贴（剪贴板含图片时），否则放行默认文本粘贴。
/// </summary>
internal sealed class ChatInputField : TextField
{
    /// <summary>
    /// 用户按下 Enter 时触发（基类已存在 Accepted 事件，这里使用独立命名避免遮蔽）。
    /// </summary>
    public event Action? SubmitPressed;

    /// <summary>
    /// 用户按下 Ctrl+V 且剪贴板含图片时触发；回调返回 <see langword="true" /> 表示按键已被消费，
    /// 返回 <see langword="false" /> 表示剪贴板无图片，放行默认文本粘贴。
    /// </summary>
    public event Func<bool>? PasteImageRequested;

    /// <inheritdoc />
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Enter)
        {
            SubmitPressed?.Invoke();
            return true;
        }

        // Esc 仅清除当前文本草稿并消费按键，避免触发 Terminal.Gui 默认 Cancel 行为导致输入焦点失效。
        // 待发送图片由窗口单独管理，不在此处清除，用户仍可继续输入说明后发送。
        if (key == Key.Esc)
        {
            Text = string.Empty;
            return true;
        }

        // Ctrl+V 优先尝试图片粘贴：剪贴板含图片时消费按键，否则交给基类走文本粘贴
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
