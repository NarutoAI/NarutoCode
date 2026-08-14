using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Messages;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 会话入口窗口：Hub（继续最近/查看历史/新建）与历史列表两个页面。
/// 用户选择后通过 SelectionResult 输出结果并停止会话。
/// </summary>
internal sealed class SessionLauncherWindow : Window
{
    private readonly IApplication app;
    private readonly SessionLauncherState state;
    private readonly List<Label> contentLabels = [];
    private readonly Label dividerLabel = new();
    private const int HistoryHeaderRowCount = 5;
    private const int HistoryFooterRowCount = 3;
    private const int WheelSelectionStep = 3;
    private bool selectionMade;

    /// <summary>
    /// 用户做出的最终选择；窗口停止后读取。
    /// </summary>
    public SessionLauncherResult SelectionResult { get; private set; } = SessionLauncherResult.Exit();

    /// <summary>
    /// 创建会话入口窗口。
    /// </summary>
    /// <param name="app">Terminal.Gui 应用实例（用于停止当前会话）。</param>
    /// <param name="workDirectory">当前工作目录。</param>
    /// <param name="conversations">当前目录下的会话摘要。</param>
    public SessionLauncherWindow(IApplication app, string workDirectory, IReadOnlyList<ConversationSummary> conversations)
    {
        this.app = app;
        BorderStyle = LineStyle.None;
        SetScheme(TuiStyles.GetCanvasScheme());

        // 顶层窗口必须显式填满屏幕，否则默认 Dim.Auto 尺寸为 0，子视图全部被裁剪（表现为控制台空白）
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        state = new SessionLauncherState(workDirectory, conversations);

        // 品牌分隔线独立于内容行：文本在布局时按窗口宽度重新生成，保证缩放后铺满无空白
        dividerLabel.X = 2;
        dividerLabel.Y = 1;
        dividerLabel.Width = Dim.Fill(Dim.Absolute(2));
        dividerLabel.SetScheme(TuiStyles.GetDividerScheme());
        Add(dividerLabel);

        Refresh();
    }

    /// <summary>
    /// 布局完成或终端尺寸变化后触发：按当前宽度重新生成分隔线。
    /// </summary>
    /// <param name="e">布局事件参数。</param>
    protected override void OnSubViewsLaidOut(LayoutEventArgs e)
    {
        base.OnSubViewsLaidOut(e);
        RefreshDivider();

        // 终端缩小时重新夹紧视窗，避免当前高亮会话落到可见范围之外。
        if (state.IsHistoryMode && state.EnsureHistorySelectionVisible(GetVisibleHistoryItemCount()))
        {
            Refresh();
        }
    }

    /// <inheritdoc />
    protected override bool OnKeyDown(Key key)
    {
        var handled = true;

        if (state.IsHistoryMode)
        {
            if (key == Key.CursorUp)
            {
                MoveHistorySelection(-1);
            }
            else if (key == Key.CursorDown)
            {
                MoveHistorySelection(1);
            }
            else if (key == Key.PageUp)
            {
                MoveHistorySelection(-GetVisibleHistoryItemCount());
            }
            else if (key == Key.PageDown)
            {
                MoveHistorySelection(GetVisibleHistoryItemCount());
            }
            else if (key == Key.Home)
            {
                state.SelectFirstHistoryItem();
                state.EnsureHistorySelectionVisible(GetVisibleHistoryItemCount());
            }
            else if (key == Key.End)
            {
                state.SelectLastHistoryItem();
                state.EnsureHistorySelectionVisible(GetVisibleHistoryItemCount());
            }
            else if (key == Key.N)
            {
                Select(SessionLauncherResult.NewConversation());
            }
            else if (key == Key.Enter && state.Conversations.Count > 0)
            {
                Select(SessionLauncherResult.Existing(
                    new ConversationSessionId(state.Conversations[state.SelectedHistoryIndex].Id)));
            }
            else if (key == Key.Esc)
            {
                state.ReturnToHub();
            }
            else
            {
                handled = false;
            }
        }
        else
        {
            if (key == Key.CursorUp)
            {
                state.MoveHubSelection(-1);
            }
            else if (key == Key.CursorDown)
            {
                state.MoveHubSelection(1);
            }
            else if (key == Key.N)
            {
                Select(SessionLauncherResult.NewConversation());
            }
            else if (key == Key.Esc)
            {
                Select(SessionLauncherResult.Exit());
            }
            else if (key == Key.Enter)
            {
                Select(ResolveHubSelection());
            }
            else
            {
                handled = false;
            }
        }

        if (!handled)
        {
            return base.OnKeyDown(key);
        }

        if (!selectionMade)
        {
            Refresh();
        }

        return true;
    }

    /// <inheritdoc />
    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (!state.IsHistoryMode)
        {
            return base.OnMouseEvent(mouse);
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            MoveHistorySelection(-WheelSelectionStep);
            Refresh();
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            MoveHistorySelection(WheelSelectionStep);
            Refresh();
            return true;
        }

        return base.OnMouseEvent(mouse);
    }

    private SessionLauncherResult ResolveHubSelection()
    {
        return state.SelectedHubOption switch
        {
            SessionLauncherOption.ContinueRecent => state.RecentConversation is null
                ? SessionLauncherResult.NewConversation()
                : SessionLauncherResult.Existing(new ConversationSessionId(state.RecentConversation.Id)),
            SessionLauncherOption.ViewHistory => EnterHistoryOrCreate(state),
            SessionLauncherOption.NewConversation => SessionLauncherResult.NewConversation(),
            _ => SessionLauncherResult.Exit()
        };
    }

    private static SessionLauncherResult EnterHistoryOrCreate(SessionLauncherState state)
    {
        if (state.Conversations.Count == 0)
        {
            return SessionLauncherResult.NewConversation();
        }

        state.EnterHistoryMode();
        return SessionLauncherResult.Exit();
    }

    private void Select(SessionLauncherResult result)
    {
        if (result.ShouldExit && state.IsHistoryMode && !selectionMade)
        {
            // ViewHistory 进入历史页时返回 Exit 仅表示"切页"，不停止窗口
            return;
        }

        selectionMade = true;
        SelectionResult = result;
        app.RequestStop(this);
    }

    /// <summary>
    /// 重建页面内容（Hub 或历史列表）。
    /// </summary>
    private void Refresh()
    {
        foreach (var label in contentLabels)
        {
            Remove(label);
        }

        contentLabels.Clear();
        RefreshDivider();

        var rows = state.IsHistoryMode ? BuildHistoryRows() : BuildHubRows();
        // 第 0 行品牌头、第 1 行分隔线（独立 Label），内容从第 2 行开始
        var y = 2;
        foreach (var (text, style) in rows)
        {
            var label = new Label
            {
                Text = text,
                X = 2,
                Y = y,
                Width = Dim.Fill(Dim.Absolute(2)),
                Height = 1
            };
            label.SetScheme(TuiStyles.GetScheme(style));
            contentLabels.Add(label);
            Add(label);
            y++;
        }

        SetNeedsDraw();
    }

    /// <summary>
    /// 按窗口当前宽度重新生成品牌分隔线，保证终端缩放后分隔线始终铺满且无空白。
    /// </summary>
    private void RefreshDivider()
    {
        // 分隔线 X=2 且 Width=Fill(-2)，文本长度取可用宽度
        var width = Math.Max(1, Viewport.Width - 4);
        dividerLabel.Text = new string('─', width);
    }

    /// <summary>
    /// 移动历史会话选中项，并让选中项始终留在当前可见范围中。
    /// </summary>
    /// <param name="delta">选中项移动数量。</param>
    private void MoveHistorySelection(int delta)
    {
        state.MoveHistorySelection(delta);
        state.EnsureHistorySelectionVisible(GetVisibleHistoryItemCount());
    }

    /// <summary>
    /// 计算当前终端高度能够完整展示的历史会话数量。
    /// 每个会话固定占两行，头部和底部操作提示保持固定。
    /// </summary>
    private int GetVisibleHistoryItemCount()
    {
        var availableRowCount = Math.Max(2, Viewport.Height - 2 - HistoryHeaderRowCount - HistoryFooterRowCount);
        return Math.Max(1, availableRowCount / 2);
    }

    private IReadOnlyList<(string Text, UiTextStyle Style)> BuildHubRows()
    {
        var rows = new List<(string, UiTextStyle)>
        {
            ("◆ NarutoCode", UiTextStyle.AccentStrong),
            ($"{state.WorkDirectory}", UiTextStyle.Muted),
            (string.Empty, UiTextStyle.Subtle),
            ("sessions", UiTextStyle.Subtle),
            (string.Empty, UiTextStyle.Subtle)
        };

        AddHubOption(rows, 0, "继续最近会话", state.RecentConversation is null
            ? "当前目录暂无历史会话，回车将新建会话"
            : $"{FormatRelativeTime(state.RecentConversation.UpdatedAt)} · {state.RecentConversation.MessageCount} 条消息 · {FormatTokenUsage(state.RecentConversation)} · {FormatPreview(state.RecentConversation)}");
        AddHubOption(rows, 1, "查看历史会话", $"当前目录共 {state.Conversations.Count} 个会话");
        AddHubOption(rows, 2, "新建会话", "保留历史，创建一个新的聊天上下文");

        rows.Add((string.Empty, UiTextStyle.Subtle));
        rows.Add(("↑↓ navigate    Enter select    n new    Esc exit", UiTextStyle.Subtle));
        return rows;
    }

    private void AddHubOption(List<(string Text, UiTextStyle Style)> rows, int index, string title, string description)
    {
        var selected = index == state.SelectedHubIndex;
        var marker = selected ? "❯ " : "  ";
        var titleStyle = selected ? UiTextStyle.AccentStrong : UiTextStyle.Normal;
        var descriptionStyle = selected ? UiTextStyle.Muted : UiTextStyle.Subtle;
        rows.Add(($" {marker}{title}", titleStyle));
        rows.Add(($"     {description}", descriptionStyle));
    }

    private IReadOnlyList<(string Text, UiTextStyle Style)> BuildHistoryRows()
    {
        var rows = new List<(string, UiTextStyle)>
        {
            ("◆ NarutoCode", UiTextStyle.AccentStrong),
            ($"{state.WorkDirectory}", UiTextStyle.Muted),
            (string.Empty, UiTextStyle.Subtle),
            ("history", UiTextStyle.Subtle),
            (string.Empty, UiTextStyle.Subtle)
        };

        if (state.Conversations.Count == 0)
        {
            rows.Add(("当前目录还没有历史会话。按 n 新建会话，或按 Esc 返回。", UiTextStyle.Muted));
        }
        else
        {
            var visibleItemCount = GetVisibleHistoryItemCount();
            state.EnsureHistorySelectionVisible(visibleItemCount);
            var endIndex = Math.Min(state.Conversations.Count, state.HistoryStartIndex + visibleItemCount);
            for (var index = state.HistoryStartIndex; index < endIndex; index++)
            {
                AddHistoryItem(rows, index);
            }

            rows.Add(($"{state.HistoryStartIndex + 1}–{endIndex} / {state.Conversations.Count} 条会话", UiTextStyle.Subtle));
        }

        rows.Add(("↑↓ select  PgUp/PgDn page  Home/End jump  wheel scroll", UiTextStyle.Subtle));
        rows.Add(("Enter select    n new    Esc back", UiTextStyle.Subtle));
        return rows;
    }

    private void AddHistoryItem(List<(string Text, UiTextStyle Style)> rows, int index)
    {
        var summary = state.Conversations[index];
        var selected = index == state.SelectedHistoryIndex;
        var marker = selected ? "❯ " : "  ";
        var titleStyle = selected ? UiTextStyle.AccentStrong : UiTextStyle.Normal;
        var metaStyle = selected ? UiTextStyle.Muted : UiTextStyle.Subtle;
        var preview = string.IsNullOrWhiteSpace(summary.LastUserMessagePreview)
            ? "暂无用户消息"
            : summary.LastUserMessagePreview;

        rows.Add(($" {marker}{summary.Title}", titleStyle));
        rows.Add(($"     {FormatRelativeTime(summary.UpdatedAt)}  |  {summary.MessageCount} 条消息  |  {FormatTokenUsage(summary)}  |  {preview}", metaStyle));
    }

    private static string FormatTokenUsage(ConversationSummary summary)
    {
        return $"总计 {FormatTokenCount(summary.TokenCount)} · 最近 {FormatTokenCount(summary.LastUsageTokenCount)}";
    }

    private static string FormatTokenCount(long tokenCount)
    {
        return tokenCount <= 0 ? "0 tokens" : $"{tokenCount:N0} tokens";
    }

    private static string FormatPreview(ConversationSummary summary)
    {
        return string.IsNullOrWhiteSpace(summary.LastUserMessagePreview)
            ? summary.Title
            : summary.LastUserMessagePreview;
    }

    private static string FormatRelativeTime(DateTime updatedAt)
    {
        var elapsed = DateTime.Now - updatedAt;
        if (elapsed.TotalMinutes < 1)
        {
            return "刚刚";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前";
        }

        if (elapsed.TotalDays < 1)
        {
            return $"{Math.Max(1, (int)elapsed.TotalHours)} 小时前";
        }

        return updatedAt.ToString("MM-dd HH:mm");
    }
}
