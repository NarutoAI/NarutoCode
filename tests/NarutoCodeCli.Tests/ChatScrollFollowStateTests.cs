using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarutoCodeCli.Ui;

namespace NarutoCodeCli.Tests;

/// <summary>
/// 验证消息视图滚动跟随策略：底部自动跟随、上翻暂停跟随、任务完成不拉回顶部、回到底部恢复。
/// </summary>
[TestClass]
public class ChatScrollFollowStateTests
{
    [TestMethod]
    public void 初始状态_位于底部且无未读()
    {
        var state = new ChatScrollFollowState();

        Assert.IsTrue(state.IsFollowing);
        Assert.IsFalse(state.HasUnread);
    }

    [TestMethod]
    public void 内容增加且位于底部_目标滚动到底部()
    {
        var state = new ChatScrollFollowState();

        // 视口 10 行，内容从 10 行增长到 30 行，原偏移 0
        var target = state.OnContentChanged(
            oldContentHeight: 10,
            oldViewportHeight: 10,
            oldScrollOffset: 0,
            newContentHeight: 30,
            newViewportHeight: 10);

        Assert.AreEqual(20, target, "跟随状态下内容增长应滚动到新底部");
        Assert.IsTrue(state.IsFollowing);
        Assert.IsFalse(state.HasUnread);
    }

    [TestMethod]
    public void 用户上翻后_内容增加不改变滚动位置()
    {
        var state = new ChatScrollFollowState();

        // 用户从底部（偏移 20）上翻到偏移 5，视为离开底部
        state.OnUserScroll(scrollOffset: 5, viewportHeight: 10, contentHeight: 30);
        Assert.IsFalse(state.IsFollowing);
        Assert.IsTrue(state.HasUnread);

        // 任务完成帧：内容增加到 40 行，不应把用户拉回顶部或跳到底部
        var target = state.OnContentChanged(
            oldContentHeight: 30,
            oldViewportHeight: 10,
            oldScrollOffset: 5,
            newContentHeight: 40,
            newViewportHeight: 10);

        Assert.AreEqual(5, target, "离开底部后内容变化应保持当前偏移");
        Assert.IsTrue(state.HasUnread, "离开底部期间产生新内容应保持未读标记");
    }

    [TestMethod]
    public void 任务完成刷新_用户停留在中间位置不回顶()
    {
        var state = new ChatScrollFollowState();

        // 长会话：视口 10，内容 200，用户上翻后停在偏移 100
        state.OnUserScroll(scrollOffset: 100, viewportHeight: 10, contentHeight: 200);

        // 任务完成帧：内容只增长几行
        var target = state.OnContentChanged(
            oldContentHeight: 200,
            oldViewportHeight: 10,
            oldScrollOffset: 100,
            newContentHeight: 204,
            newViewportHeight: 10);

        Assert.AreEqual(100, target);
        Assert.AreNotEqual(0, target, "不得把视口拉回顶部");
        Assert.AreNotEqual(194, target, "离开底部时不得自动跳到底部");
    }

    [TestMethod]
    public void 回到底部后_恢复自动跟随并清除未读()
    {
        var state = new ChatScrollFollowState();

        state.OnUserScroll(scrollOffset: 3, viewportHeight: 10, contentHeight: 30);
        Assert.IsFalse(state.IsFollowing);
        Assert.IsTrue(state.HasUnread);

        // End 键回到底部
        state.ScrollToBottom();
        Assert.IsTrue(state.IsFollowing);
        Assert.IsFalse(state.HasUnread);

        // 后续内容变化恢复跟随
        var target = state.OnContentChanged(
            oldContentHeight: 30,
            oldViewportHeight: 10,
            oldScrollOffset: 20,
            newContentHeight: 40,
            newViewportHeight: 10);
        Assert.AreEqual(30, target);
    }

    [TestMethod]
    public void 内容高度小于视口_始终视为底部()
    {
        var state = new ChatScrollFollowState();

        // 内容 5 行 < 视口 20 行：任何偏移都算在底部
        var atBottom = state.OnUserScroll(scrollOffset: 0, viewportHeight: 20, contentHeight: 5);
        Assert.IsTrue(atBottom);
        Assert.IsFalse(state.HasUnread);

        var target = state.OnContentChanged(
            oldContentHeight: 5,
            oldViewportHeight: 20,
            oldScrollOffset: 0,
            newContentHeight: 8,
            newViewportHeight: 20);
        Assert.AreEqual(0, target, "内容小于视口时目标偏移应为 0");
    }

    [TestMethod]
    public void 视口变小时_离开底部的偏移被夹紧到有效范围()
    {
        var state = new ChatScrollFollowState();

        // 内容 100 行、视口 10 行时用户停在偏移 90；视口缩小为 5 行
        state.OnUserScroll(scrollOffset: 90, viewportHeight: 10, contentHeight: 100);
        var target = state.OnContentChanged(
            oldContentHeight: 100,
            oldViewportHeight: 10,
            oldScrollOffset: 90,
            newContentHeight: 100,
            newViewportHeight: 5);

        Assert.AreEqual(95, target, "偏移应被夹紧到新的最大偏移，而不是越界或回顶");
    }
}
