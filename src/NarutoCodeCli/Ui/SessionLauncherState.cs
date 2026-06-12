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

        SelectedHistoryIndex = (SelectedHistoryIndex + delta + Conversations.Count) % Conversations.Count;
    }

    /// <summary>
    /// 进入历史会话列表页。
    /// </summary>
    public void EnterHistoryMode()
    {
        IsHistoryMode = true;
        SelectedHistoryIndex = 0;
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
