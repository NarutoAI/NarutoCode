using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 可滚动的聊天消息视图：每条消息行是一个 Label，按内容坐标系排列；
/// 通过 Viewport 虚拟滚动（滚轮、PgUp/PgDn、Home/End）。
/// 位于底部时自动跟随新内容；手动上翻后暂停跟随并产生未读标记。
/// </summary>
internal sealed class ChatMessageListView : View
{
    private const int WheelScrollStep = 3;
    private const int KeyScrollStep = 1;

    private readonly ChatScrollFollowState followState = new();
    private readonly List<StyledLine> renderedLines = [];
    private readonly List<int> messageLineStarts = [];
    private readonly List<int> renderedVersions = [];
    private readonly List<Label> lineLabels = [];
    private IReadOnlyList<ChatMessage>? messages;
    private int lastRenderWidth;
    private int lastViewportHeight;
    private int renderedMessageCount;

    /// <summary>
    /// 当前是否存在未读新消息（用户离开底部后产生的）。
    /// </summary>
    public bool HasUnread => followState.HasUnread;

    /// <summary>
    /// 跟随状态或未读状态变化事件，用于刷新底部提示。
    /// </summary>
    public event Action? StateChanged;

    /// <summary>
    /// 创建可滚动的消息列表视图。
    /// </summary>
    public ChatMessageListView()
    {
        CanFocus = true;
        SetScheme(TuiStyles.GetCanvasScheme());
    }

    /// <summary>
    /// 更新消息内容；仅在跟随底部时自动滚动到最新内容。
    /// </summary>
    /// <param name="messages">当前会话消息列表。</param>
    public void UpdateMessages(IReadOnlyList<ChatMessage> messages)
    {
        this.messages = messages;

        // 布局完成前 Viewport 可能为 0，用默认宽度兜底，下一次更新会纠正
        var width = Math.Max(16, Viewport.Width);
        lastRenderWidth = width;
        var oldContentHeight = GetContentSize().Height;
        var oldScrollOffset = Viewport.Location.Y;

        SyncLines(messages, width);

        // 更新虚拟内容尺寸，触发滚动范围与重排
        SetContentSize(new Size(width, renderedLines.Count));
        SetNeedsLayout();
        SetNeedsDraw();

        // 内容变化后按跟随策略计算目标滚动偏移
        var viewportHeight = Math.Max(1, Viewport.Height);
        var target = followState.OnContentChanged(
            oldContentHeight,
            viewportHeight,
            oldScrollOffset,
            renderedLines.Count,
            viewportHeight);
        ScrollToOffset(target);
        SetNeedsDraw();
    }

    /// <summary>
    /// 布局完成或终端尺寸变化后触发：可用列宽变化时按新宽度重排全部消息行，
    /// 避免窗口缩放后文本仍按旧宽度折行导致右侧空白或被截断。
    /// </summary>
    /// <param name="e">布局事件参数。</param>
    protected override void OnSubViewsLaidOut(LayoutEventArgs e)
    {
        base.OnSubViewsLaidOut(e);

        // 视口高度变化（交互抽屉预留/恢复底部空间）：原本贴底显示时滚动到新底部，保持最新内容可见
        var currentViewportHeight = Math.Max(1, Viewport.Height);
        if (currentViewportHeight != lastViewportHeight)
        {
            // 用变化前的视口高度判断是否原本贴底（内容高度未变，仅视口伸缩）
            var wasAtBottom = Viewport.Location.Y >= Math.Max(0, GetContentSize().Height - lastViewportHeight);
            lastViewportHeight = currentViewportHeight;
            if (wasAtBottom && messages is { Count: > 0 })
            {
                ScrollToOffset(int.MaxValue);
                SetNeedsDraw();
            }
        }

        // 尚无消息时仅记录宽度，等首条消息到达时按真实宽度渲染
        if (messages is null || messages.Count == 0)
        {
            lastRenderWidth = Math.Max(16, Viewport.Width);
            return;
        }

        var width = Math.Max(16, Viewport.Width);
        if (width == lastRenderWidth)
        {
            return;
        }

        // 列宽变化：保留旧滚动位置比例，按新宽度全量重排
        var oldContentHeight = GetContentSize().Height;
        var oldScrollOffset = Viewport.Location.Y;
        lastRenderWidth = width;

        RebuildAll(messages, width);
        renderedMessageCount = messages.Count;
        renderedVersions.Clear();
        renderedVersions.AddRange(messages.Select(message => message.RenderVersion));

        SetContentSize(new Size(width, renderedLines.Count));
        SetNeedsLayout();
        SetNeedsDraw();

        var viewportHeight = Math.Max(1, Viewport.Height);
        var target = followState.OnContentChanged(
            oldContentHeight,
            viewportHeight,
            oldScrollOffset,
            renderedLines.Count,
            viewportHeight);
        ScrollToOffset(target);
        SetNeedsDraw();
    }

    /// <summary>
    /// 恢复到底部并清除未读标记。
    /// </summary>
    public void ScrollToBottom()
    {
        followState.ScrollToBottom();
        ScrollToOffset(int.MaxValue);
        StateChanged?.Invoke();
        SetNeedsDraw();
    }

    protected override bool OnKeyDown(Key key)
    {
        return ScrollByKey(key) || base.OnKeyDown(key);
    }

    /// <summary>
    /// 按滚动按键滚动消息区：↑↓ 微调、PgUp/PgDn 翻页、Home/End 跳转首尾。
    /// 供本视图键盘事件与交互弹窗转发（弹窗模态期间用户仍可滚动查看历史消息）复用。
    /// </summary>
    /// <param name="key">滚动按键。</param>
    /// <returns>按键属于滚动键并被消费时返回 <see langword="true" />。</returns>
    public bool ScrollByKey(Key key)
    {
        var viewportHeight = Math.Max(1, Viewport.Height);
        var contentHeight = GetContentSize().Height;
        var current = Viewport.Location.Y;
        var target = current;

        if (key == Key.CursorUp)
        {
            target = current - KeyScrollStep;
        }
        else if (key == Key.CursorDown)
        {
            target = current + KeyScrollStep;
        }
        else if (key == Key.PageUp)
        {
            target = current - viewportHeight + 1;
        }
        else if (key == Key.PageDown)
        {
            target = current + viewportHeight - 1;
        }
        else if (key == Key.Home)
        {
            target = 0;
        }
        else if (key == Key.End)
        {
            followState.ScrollToBottom();
            target = Math.Max(0, contentHeight - viewportHeight);
        }
        else
        {
            return false;
        }

        ScrollToOffset(target);
        followState.OnUserScroll(Viewport.Location.Y, viewportHeight, contentHeight);
        StateChanged?.Invoke();
        return true;
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            ScrollBy(-WheelScrollStep);
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            ScrollBy(WheelScrollStep);
            return true;
        }

        return base.OnMouseEvent(mouse);
    }

    /// <summary>
    /// 滚轮滚动消息区；供交互弹窗模态期间转发滚轮事件复用。
    /// </summary>
    /// <param name="up">是否向上滚动（查看更早消息）。</param>
    public void ScrollByWheel(bool up)
    {
        ScrollBy(up ? -WheelScrollStep : WheelScrollStep);
    }

    private void ScrollBy(int delta)
    {
        var viewportHeight = Math.Max(1, Viewport.Height);
        var contentHeight = GetContentSize().Height;
        ScrollToOffset(Viewport.Location.Y + delta);
        followState.OnUserScroll(Viewport.Location.Y, viewportHeight, contentHeight);
        StateChanged?.Invoke();
    }

    private void ScrollToOffset(int target)
    {
        var viewportHeight = Math.Max(1, Viewport.Height);
        var contentHeight = GetContentSize().Height;
        var maxOffset = Math.Max(0, contentHeight - viewportHeight);
        var clamped = Math.Clamp(target, 0, maxOffset);
        var current = Viewport.Location.Y;
        if (clamped == current)
        {
            return;
        }

        ScrollVertical(clamped - current);
    }

    /// <summary>
    /// 增量同步消息行：仅最后一条消息变化时替换其行，仅新增一条时追加，其余情况全量重建。
    /// </summary>
    private void SyncLines(IReadOnlyList<ChatMessage> messages, int width)
    {
        if (messages.Count == 0)
        {
            if (renderedMessageCount != 0)
            {
                ClearAll();
                renderedMessageCount = 0;
            }

            return;
        }

        var diffIndex = FirstVersionDiffIndex(messages);
        if (diffIndex < 0)
        {
            // 内容未变化，但仍要确保活动指示器按当前状态更新
            return;
        }

        if (diffIndex == messages.Count - 1 && renderedVersions.Count == messages.Count)
        {
            // 仅最后一条消息变化（流式输出场景）：增量替换最后一条消息的行
            ReplaceLastMessage(messages[^1], width);
        }
        else if (diffIndex == renderedVersions.Count && renderedVersions.Count == messages.Count - 1)
        {
            // 新增一条消息：只追加
            AppendMessage(messages[^1], width);
        }
        else
        {
            // 结构变化：全量重建
            RebuildAll(messages, width);
        }

        renderedMessageCount = messages.Count;
        renderedVersions.Clear();
        renderedVersions.AddRange(messages.Select(message => message.RenderVersion));
    }

    /// <summary>
    /// 返回首个内容版本发生变化的消息下标；无变化返回 -1。
    /// </summary>
    private int FirstVersionDiffIndex(IReadOnlyList<ChatMessage> messages)
    {
        var overlap = Math.Min(renderedVersions.Count, messages.Count);
        for (var index = 0; index < overlap; index++)
        {
            if (renderedVersions[index] != messages[index].RenderVersion)
            {
                return index;
            }
        }

        return renderedVersions.Count == messages.Count ? -1 : renderedVersions.Count;
    }

    private void ReplaceLastMessage(ChatMessage message, int width)
    {
        var start = messageLineStarts[^1];
        renderedLines.RemoveRange(start, renderedLines.Count - start);
        var newLines = ChatMessageLineBuilder.Build(message, width);
        renderedLines.AddRange(newLines);
        SynchronizeLineLabels(start);
    }

    private void AppendMessage(ChatMessage message, int width)
    {
        messageLineStarts.Add(renderedLines.Count);
        var start = renderedLines.Count;
        var newLines = ChatMessageLineBuilder.Build(message, width);
        renderedLines.AddRange(newLines);
        SynchronizeLineLabels(start);
    }

    private void RebuildAll(IReadOnlyList<ChatMessage> messages, int width)
    {
        renderedLines.Clear();
        messageLineStarts.Clear();
        foreach (var message in messages)
        {
            messageLineStarts.Add(renderedLines.Count);
            var lines = ChatMessageLineBuilder.Build(message, width);
            renderedLines.AddRange(lines);
        }

        SynchronizeLineLabels(0);
    }

    private void ClearAll()
    {
        foreach (var label in lineLabels)
        {
            Remove(label);
        }

        lineLabels.Clear();
        renderedLines.Clear();
        messageLineStarts.Clear();
    }

    /// <summary>
    /// 将行数据同步到既有 Label：宽度变化时复用控件，只更新文本和样式，
    /// 避免横向缩放期间为全部历史行执行 Remove/Add 及其关联布局。
    /// </summary>
    /// <param name="start">需要更新的起始行。</param>
    private void SynchronizeLineLabels(int start)
    {
        while (lineLabels.Count > renderedLines.Count)
        {
            var label = lineLabels[^1];
            Remove(label);
            lineLabels.RemoveAt(lineLabels.Count - 1);
        }

        for (var index = start; index < renderedLines.Count; index++)
        {
            var line = renderedLines[index];
            if (index < lineLabels.Count)
            {
                var existingLabel = lineLabels[index];
                existingLabel.Text = line.Text;
                existingLabel.Y = index;
                existingLabel.SetScheme(TuiStyles.GetScheme(line.Style));
                continue;
            }

            var label = new Label
            {
                Text = line.Text,
                X = 0,
                Y = index,
                Width = Dim.Fill(),
                Height = 1
            };
            label.SetScheme(TuiStyles.GetScheme(line.Style));
            lineLabels.Add(label);
            Add(label);
        }
    }
}
