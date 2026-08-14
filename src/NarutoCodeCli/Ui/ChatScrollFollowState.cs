namespace NarutoCodeCli.Ui;

/// <summary>
/// 消息视图的滚动跟随策略：位于底部时自动跟随新内容，手动上翻后暂停跟随并产生未读标记。
/// 纯逻辑实现，不依赖任何 UI 框架，便于单元测试。
/// </summary>
internal sealed class ChatScrollFollowState
{
    /// <summary>
    /// 是否处于自动跟随状态（滚动位置在消息区底部）。
    /// </summary>
    public bool IsFollowing { get; private set; } = true;

    /// <summary>
    /// 用户离开底部后是否产生了未读内容。
    /// </summary>
    public bool HasUnread { get; private set; }

    /// <summary>
    /// 内容或视口变化后计算目标滚动偏移。
    /// </summary>
    /// <param name="oldContentHeight">变化前内容高度。</param>
    /// <param name="oldViewportHeight">变化前视口高度。</param>
    /// <param name="oldScrollOffset">变化前滚动偏移。</param>
    /// <param name="newContentHeight">变化后内容高度。</param>
    /// <param name="newViewportHeight">变化后视口高度。</param>
    /// <returns>应设置的滚动偏移；跟随状态下返回新的底部位置。</returns>
    public int OnContentChanged(
        int oldContentHeight,
        int oldViewportHeight,
        int oldScrollOffset,
        int newContentHeight,
        int newViewportHeight)
    {
        var wasAtBottom = IsAtBottom(oldScrollOffset, oldViewportHeight, oldContentHeight);
        if (IsFollowing && wasAtBottom)
        {
            return BottomOffset(newContentHeight, newViewportHeight);
        }

        // 离开底部时保持当前偏移，但夹紧到新的有效范围，避免越界跳动
        return Math.Clamp(oldScrollOffset, 0, BottomOffset(newContentHeight, newViewportHeight));
    }

    /// <summary>
    /// 用户滚动后更新跟随状态；返回滚动后是否仍在底部。
    /// </summary>
    /// <param name="scrollOffset">当前滚动偏移。</param>
    /// <param name="viewportHeight">视口高度。</param>
    /// <param name="contentHeight">内容高度。</param>
    /// <returns>仍在底部时返回 <see langword="true" />。</returns>
    public bool OnUserScroll(int scrollOffset, int viewportHeight, int contentHeight)
    {
        IsFollowing = IsAtBottom(scrollOffset, viewportHeight, contentHeight);
        HasUnread = !IsFollowing;
        return IsFollowing;
    }

    /// <summary>
    /// 用户回到底部（End 键或滚动到底）时恢复自动跟随并清除未读标记。
    /// </summary>
    public void ScrollToBottom()
    {
        IsFollowing = true;
        HasUnread = false;
    }

    private static bool IsAtBottom(int scrollOffset, int viewportHeight, int contentHeight)
    {
        if (contentHeight <= viewportHeight)
        {
            return true;
        }

        // 允许 1 行的容差，避免最后一行恰好可见时被误判为离开底部
        return scrollOffset >= contentHeight - viewportHeight - 1;
    }

    private static int BottomOffset(int contentHeight, int viewportHeight)
    {
        return Math.Max(0, contentHeight - viewportHeight);
    }
}
