namespace NarutoCode.Domain.Configurations;

/// <summary>
/// 系统运行配置，描述日志等非模型业务的基础运行参数。
/// </summary>
public sealed class SystemConfiguration
{
    /// <summary>
    /// 日志最小输出级别，支持 Verbose、Debug、Information、Warning、Error、Fatal；未配置时默认 Error。
    /// </summary>
    public string LogLevel { get; set; } = "Error";

    /// <summary>
    /// 压缩策略阈值配置
    /// </summary>
    public CompactionThresholds CompactionThresholds { get; set; } = new();
}

/// <summary>
/// 压缩策略阈值配置，定义三种压缩策略的触发阈值（相对于上下文窗口的比例）。
/// </summary>
public sealed class CompactionThresholds
{
    /// <summary>
    /// 图片压缩触发阈值（相对于上下文窗口的比例）。
    /// 图片占用空间大，优先处理，默认 0.4。
    /// </summary>
    public double ImageCompaction { get; set; } = 0.4;

    /// <summary>
    /// 工具结果压缩触发阈值（相对于上下文窗口的比例）。
    /// 不调用 LLM，轻量级压缩，默认 0.6。
    /// </summary>
    public double ToolEviction { get; set; } = 0.6;

    /// <summary>
    /// 摘要压缩触发阈值（相对于上下文窗口的比例）。
    /// 调用 LLM 生成摘要，代价最高，默认 0.8。
    /// </summary>
    public double Summarization { get; set; } = 0.8;
}
