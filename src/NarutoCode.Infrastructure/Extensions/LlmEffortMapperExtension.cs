using Microsoft.Extensions.AI;
using NarutoCode.Domain.Enums;

namespace NarutoCode.Infrastructure.Extensions;

/// <summary>
/// 
/// </summary>
internal static class LlmEffortMapperExtension
{
    extension(LlmEffort effort)
    {
        public ReasoningEffort ToReasoningEffort()
        {
            return effort switch
            {
                LlmEffort.Low => ReasoningEffort.Low,
                LlmEffort.Medium => ReasoningEffort.Medium,
                LlmEffort.High => ReasoningEffort.High,
                LlmEffort.XHigh => ReasoningEffort.ExtraHigh,
                _ => ReasoningEffort.Medium
            };
        }
    }
}