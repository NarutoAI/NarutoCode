using NarutoCode.Domain.Configurations;

namespace NarutoCode.Domain.LlmContextAccessors;

/// <summary>
/// 
/// </summary>
public interface ILlmContextAccessor
{
    /// <summary>
    /// 获取当前的上下文
    /// </summary>
    LlmContext? Current { get; set; }
}
