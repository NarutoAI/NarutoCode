using NarutoCode.Domain.Configurations;

namespace NarutoCode.Infrastructure.Tests.Stores;

/// <summary>
/// 验证上下文裁剪阈值默认策略。
/// </summary>
[TestClass]
public sealed class CompactionThresholdsTests
{
    /// <summary>
    /// 默认阈值应按图片、工具结果、摘要、兜底截断的顺序逐步触发。
    /// </summary>
    [TestMethod]
    public void Defaults_UseLayeredCompactionThresholds()
    {
        // Arrange
        var thresholds = new CompactionThresholds();

        // Assert
        Assert.AreEqual(0.25, thresholds.ImageCompaction);
        Assert.AreEqual(0.6, thresholds.ToolEviction);
        Assert.AreEqual(0.8, thresholds.Summarization);
        Assert.AreEqual(0.9, thresholds.FallbackTruncation);
        Assert.AreEqual(8, thresholds.MinimumPreservedGroups);
    }
}
