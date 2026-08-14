using NarutoCode.Domain.Conversations;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 会话入口页 UI 状态。
/// </summary>
internal sealed class SessionLauncherState
{
    private const int HubOptionCount = 3;

    /// <summary>
    /// 创建会话入口页 UI 状态。
    /// </summary>
    /// <param name="workDirectory">当前工作目录。</param>
    /// <param name="conversations">当前目录下的会话摘要。</param>
    public SessionLauncherState(string workDirectory, IReadOnlyList<ConversationSummary> conversations)
    {
        WorkDirectory = workDirectory;
        Conversations = conversations;
        SelectedHubIndex = conversations.Count == 0 ? 2 : 0;
    }

    /// <summary>
    /// 当前工作目录。
    /// </summary>
    public string WorkDirectory { get; }

    /// <summary>
    /// 当前目录下的会话摘要。
    /// </summary>
    public IReadOnlyList<ConversationSummary> Conversations { get; }

    /// <summary>
    /// 是否正在展示历史列表页。
    /// </summary>
    public bool IsHistoryMode { get; private set; }

    /// <summary>
    /// 入口 Hub 当前选中项索引。
    /// </summary>
    public int SelectedHubIndex { get; private set; }

    /// <summary>
    /// 历史列表当前选中项索引。
    /// </summary>
    public int SelectedHistoryIndex { get; private set; }

    /// <summary>
    /// 历史列表当前视窗的起始会话索引。
    /// </summary>
    public int HistoryStartIndex { get; private set; }

    /// <summary>
    /// 当前最近会话。
    /// </summary>
    public ConversationSummary? RecentConversation => Conversations.Count == 0 ? null : Conversations[0];

    /// <summary>
    /// 将入口页选择移动指定步数。
    /// </summary>
    /// <param name="delta">移动步数。</param>
    public void MoveHubSelection(int delta)
    {
        SelectedHubIndex = (SelectedHubIndex + delta + HubOptionCount) % HubOptionCount;
    }

    /// <summary>
    /// 将历史页选择移动指定步数。
    /// </summary>
    /// <param name="delta">移动步数。</param>
    public void MoveHistorySelection(int delta)
    {
        if (Conversations.Count == 0)
        {
            SelectedHistoryIndex = 0;
            return;
        }

        var targetIndex = (SelectedHistoryIndex + delta) % Conversations.Count;
        SelectedHistoryIndex = targetIndex < 0 ? targetIndex + Conversations.Count : targetIndex;
    }

    /// <summary>
    /// 将历史列表选中项定位到首个会话。
    /// </summary>
    public void SelectFirstHistoryItem()
    {
        SelectedHistoryIndex = 0;
    }

    /// <summary>
    /// 将历史列表选中项定位到最后一个会话。
    /// </summary>
    public void SelectLastHistoryItem()
    {
        SelectedHistoryIndex = Math.Max(0, Conversations.Count - 1);
    }

    /// <summary>
    /// 确保当前选中会话位于历史列表可见范围内。
    /// </summary>
    /// <param name="visibleItemCount">当前终端高度可显示的会话数量。</param>
    /// <returns>历史列表视窗起始索引是否发生变化。</returns>
    public bool EnsureHistorySelectionVisible(int visibleItemCount)
    {
        if (Conversations.Count == 0)
        {
            HistoryStartIndex = 0;
            return false;
        }

        var oldStartIndex = HistoryStartIndex;
        var visibleCount = Math.Max(1, visibleItemCount);
        var maxStartIndex = Math.Max(0, Conversations.Count - visibleCount);

        // 选中项移到视窗上方时，直接以选中项作为新的第一项。
        if (SelectedHistoryIndex < HistoryStartIndex)
        {
            HistoryStartIndex = SelectedHistoryIndex;
        }
        // 选中项移到视窗下方时，保留一整屏内容并让选中项落在最后一项。
        else if (SelectedHistoryIndex >= HistoryStartIndex + visibleCount)
        {
            HistoryStartIndex = SelectedHistoryIndex - visibleCount + 1;
        }

        HistoryStartIndex = Math.Clamp(HistoryStartIndex, 0, maxStartIndex);
        return oldStartIndex != HistoryStartIndex;
    }

    /// <summary>
    /// 进入历史会话列表页。
    /// </summary>
    public void EnterHistoryMode()
    {
        IsHistoryMode = true;
        SelectedHistoryIndex = 0;
        HistoryStartIndex = 0;
    }

    /// <summary>
    /// 返回入口 Hub。
    /// </summary>
    public void ReturnToHub()
    {
        IsHistoryMode = false;
    }

    /// <summary>
    /// 当前 Hub 选中的操作。
    /// </summary>
    public SessionLauncherOption SelectedHubOption => SelectedHubIndex switch
    {
        0 => SessionLauncherOption.ContinueRecent,
        1 => SessionLauncherOption.ViewHistory,
        2 => SessionLauncherOption.NewConversation,
        _ => SessionLauncherOption.Exit
    };
}
