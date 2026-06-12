using NarutoCode.Domain.Conversations;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 渲染会话入口 Hub 和历史会话列表。
/// </summary>
internal sealed class SessionLauncherRenderer
{
    private readonly TuiColorPalette palette = TuiColorPalettes.Current;

    /// <summary>
    /// 清屏并渲染当前会话入口页状态。
    /// </summary>
    /// <param name="state">会话入口页状态。</param>
    public void Render(SessionLauncherState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        AnsiConsole.Clear();
        AnsiConsole.Write(Build(state));
    }

    private IRenderable Build(SessionLauncherState state)
    {
        return state.IsHistoryMode
            ? BuildHistory(state)
            : BuildHub(state);
    }

    private IRenderable BuildHub(SessionLauncherState state)
    {
        var rows = new List<IRenderable>
        {
            CreateHeader(state.WorkDirectory, "session launcher"),
            new Text(string.Empty),
            new Markup($"[{palette.Ink}]今天要从哪里开始？[/]"),
            new Markup($"[{palette.Muted}]选择一个入口，继续最近工作、查看历史会话，或开启新的上下文。[/]"),
            new Text(string.Empty),
            CreateHubOption(
                0,
                state.SelectedHubIndex,
                "继续最近会话",
                state.RecentConversation is null
                    ? "当前目录暂无历史会话，回车将新建会话"
                    : $"{FormatRelativeTime(state.RecentConversation.UpdatedAt)} · {state.RecentConversation.MessageCount} 条消息 · {FormatPreview(state.RecentConversation)}"),
            CreateHubOption(
                1,
                state.SelectedHubIndex,
                "查看历史会话",
                $"当前目录共 {state.Conversations.Count} 个会话"),
            CreateHubOption(
                2,
                state.SelectedHubIndex,
                "新建会话",
                "保留历史，创建一个新的聊天上下文"),
            new Text(string.Empty),
            new Markup($"[{palette.Subtle}]↑↓ 切换入口   Enter 确认   Esc 退出[/]")
        };

        return new Rows(rows);
    }

    private IRenderable BuildHistory(SessionLauncherState state)
    {
        var rows = new List<IRenderable>
        {
            CreateHeader(state.WorkDirectory, "history"),
            new Text(string.Empty),
            new Markup($"[{palette.Ink}]历史会话[/]"),
            new Markup($"[{palette.Muted}]按最近更新时间排序，选择后进入现有聊天页。[/]"),
            new Text(string.Empty)
        };

        if (state.Conversations.Count == 0)
        {
            rows.Add(new Markup($"[{palette.Muted}]当前目录还没有历史会话。按 [bold {palette.Accent}]n[/] 新建会话，或按 Esc 返回。[/]"));
        }
        else
        {
            for (var index = 0; index < state.Conversations.Count; index++)
            {
                rows.Add(CreateHistoryItem(index, state.SelectedHistoryIndex, state.Conversations[index]));
            }
        }

        rows.Add(new Text(string.Empty));
        rows.Add(new Markup($"[{palette.Subtle}]↑↓ 选择   Enter 进入   n 新建   Esc 返回入口[/]"));
        return new Rows(rows);
    }

    private IRenderable CreateHeader(string workDirectory, string mode)
    {
        return new Rows(
            new Markup($"[bold {palette.Accent}]◆ NarutoCode[/] [{palette.Muted}]agentic coding TUI · {Markup.Escape(mode)}[/]"),
            new Markup($"[{palette.Muted}]cwd[/] [{palette.Ink}]{Markup.Escape(workDirectory)}[/]"));
    }

    private IRenderable CreateHubOption(int index, int selectedIndex, string title, string description)
    {
        var selected = index == selectedIndex;
        var marker = selected ? "❯" : " ";
        var titleStyle = selected ? $"bold {palette.Accent}" : palette.Ink;
        var markerStyle = selected ? palette.Accent : palette.Subtle;
        var descriptionStyle = selected ? palette.Muted : palette.Subtle;

        return new Rows(
            new Markup($"  [{markerStyle}]{marker}[/] [{titleStyle}]{Markup.Escape(title)}[/]"),
            new Markup($"      [{descriptionStyle}]{Markup.Escape(description)}[/]"));
    }

    private IRenderable CreateHistoryItem(int index, int selectedIndex, ConversationSummary summary)
    {
        var selected = index == selectedIndex;
        var marker = selected ? "❯" : " ";
        var markerStyle = selected ? palette.Accent : palette.Subtle;
        var titleStyle = selected ? $"bold {palette.Ink}" : palette.Ink;
        var metaStyle = selected ? palette.Muted : palette.Subtle;
        var preview = string.IsNullOrWhiteSpace(summary.LastUserMessagePreview)
            ? "暂无用户消息"
            : summary.LastUserMessagePreview;

        return new Rows(
            new Markup($"  [{markerStyle}]{marker}[/] [{titleStyle}]{Markup.Escape(summary.Title)}[/]"),
            new Markup($"      [{metaStyle}]{FormatRelativeTime(summary.UpdatedAt)} · {summary.MessageCount} 条消息 · {Markup.Escape(preview)}[/]"));
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
            return $"{Math.Max(1, (int) elapsed.TotalMinutes)} 分钟前";
        }

        if (elapsed.TotalDays < 1)
        {
            return $"{Math.Max(1, (int) elapsed.TotalHours)} 小时前";
        }

        return updatedAt.ToString("MM-dd HH:mm");
    }
}
