namespace NarutoCode.Domain.Configurations;

/// <summary>
/// 当前异步调用链使用的 LLM 上下文。
/// </summary>
public sealed class LlmContext
{
    /// <summary>
    /// 当前异步调用链使用的模型供应商。
    /// </summary>
    public string Provider { get; set; } = string.Empty;
}
