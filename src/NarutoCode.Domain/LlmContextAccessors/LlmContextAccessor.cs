using NarutoCode.Domain.Configurations;

namespace NarutoCode.Domain.LlmContextAccessors;

/// <summary>
/// 
/// </summary>
public sealed class LlmContextAccessor : ILlmContextAccessor
{
    private static readonly AsyncLocal<LlmContext?> Context = new();


    public LlmContext? Current
    {
        get => Context.Value;
        set
        {
            //清空
            Context.Value = null;
            if (value is not null)
            {
                Context.Value = value;
            }
        }
    }
}