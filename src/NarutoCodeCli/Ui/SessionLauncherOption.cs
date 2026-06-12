namespace NarutoCodeCli.Ui;

/// <summary>
/// 会话入口页可选择的操作。
/// </summary>
internal enum SessionLauncherOption
{
    /// <summary>
    /// 继续最近一次会话。
    /// </summary>
    ContinueRecent,

    /// <summary>
    /// 查看历史会话列表。
    /// </summary>
    ViewHistory,

    /// <summary>
    /// 创建新的会话。
    /// </summary>
    NewConversation,

    /// <summary>
    /// 退出应用。
    /// </summary>
    Exit
}
