using Microsoft.Extensions.AI;
using NarutoCode.Infrastructure.AIAgents.ChatHistorys;

namespace NarutoCode.Infrastructure.Tests.AIAgents.ChatHistorys;

/// <summary>
/// 验证不支持视觉的模型读取聊天历史时对图片内容的过滤行为。
/// </summary>
[TestClass]
public sealed class PersistenceChatHistoryProviderVisionTests
{
    /// <summary>
    /// 图片 + 文本消息：图片替换为 [image] 占位，文本保留，且不修改原始消息。
    /// </summary>
    [TestMethod]
    public void FilterVisionUnsupportedMessages_WhenMessageHasImageAndText_ReplacesImageWithPlaceholder()
    {
        // Arrange
        var original = CreateMessage("你好", hasImage: true);

        // Act
        var result = PersistenceChatHistoryProvider.FilterVisionUnsupportedMessages([original]);

        // Assert
        Assert.HasCount(1, result);
        var filtered = result[0];
        Assert.HasCount(2, filtered.Contents);
        Assert.IsInstanceOfType(filtered.Contents[0], typeof(TextContent));
        Assert.AreEqual("你好", ((TextContent)filtered.Contents[0]).Text);
        Assert.IsInstanceOfType(filtered.Contents[1], typeof(TextContent));
        Assert.AreEqual("[image]", ((TextContent)filtered.Contents[1]).Text);
        // 原始消息未被修改，图片内容仍保留
        Assert.IsInstanceOfType(original.Contents[1], typeof(DataContent));
    }

    /// <summary>
    /// 仅图片消息：过滤后保留 [image] 占位，避免模型收到空内容消息。
    /// </summary>
    [TestMethod]
    public void FilterVisionUnsupportedMessages_WhenMessageHasOnlyImage_KeepsPlaceholder()
    {
        // Arrange
        var original = CreateMessage(content: null, hasImage: true);

        // Act
        var result = PersistenceChatHistoryProvider.FilterVisionUnsupportedMessages([original]);

        // Assert
        Assert.HasCount(1, result);
        var filtered = result[0];
        Assert.HasCount(1, filtered.Contents);
        Assert.IsInstanceOfType(filtered.Contents[0], typeof(TextContent));
        Assert.AreEqual("[image]", ((TextContent)filtered.Contents[0]).Text);
    }

    /// <summary>
    /// 无图片消息：原样返回，不产生新的消息实例。
    /// </summary>
    [TestMethod]
    public void FilterVisionUnsupportedMessages_WhenMessageHasNoImage_ReturnsOriginalInstances()
    {
        // Arrange
        var original = CreateMessage("纯文本", hasImage: false);

        // Act
        var result = PersistenceChatHistoryProvider.FilterVisionUnsupportedMessages([original]);

        // Assert
        Assert.HasCount(1, result);
        Assert.AreSame(original, result[0]);
        Assert.IsInstanceOfType(result[0].Contents[0], typeof(TextContent));
    }

    /// <summary>
    /// 过滤返回新集合，原消息实例与图片内容均保持不变，切回视觉模型后历史仍可用。
    /// </summary>
    [TestMethod]
    public void FilterVisionUnsupportedMessages_DoesNotMutateOriginalMessage()
    {
        // Arrange
        var original = CreateMessage("带图片", hasImage: true);
        var originalContents = original.Contents;

        // Act
        _ = PersistenceChatHistoryProvider.FilterVisionUnsupportedMessages([original]);

        // Assert
        Assert.AreSame(originalContents, original.Contents);
        Assert.HasCount(2, original.Contents);
        Assert.IsInstanceOfType(original.Contents[1], typeof(DataContent));
    }

    /// <summary>
    /// 构造包含可选文本和图片的聊天消息。
    /// </summary>
    private static ChatMessage CreateMessage(string? content, bool hasImage)
    {
        var contents = new List<AIContent>();
        if (content is not null)
        {
            contents.Add(new TextContent(content));
        }

        if (hasImage)
        {
            contents.Add(new DataContent(new byte[] { 1, 2, 3 }, "image/png"));
        }

        return new ChatMessage(ChatRole.User, contents);
    }
}
