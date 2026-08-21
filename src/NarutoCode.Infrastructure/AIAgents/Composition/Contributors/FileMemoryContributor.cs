#pragma warning disable MAAI001
using Microsoft.Agents.AI;
using NarutoCode.Domain;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// 文件记忆贡献者：为当前会话挂载基于文件的工作记忆（与其它会话隔离），经工具延续回合跳过包装。
/// </summary>
public sealed class FileMemoryContributor : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "FileMemory";

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        // 会话工作记忆存储于工作目录的 .narutocode/memory 下
        var memoryPath = Path.Combine(context.WorkingDirectory, ProjectConstant.ConfigurationDirectory, "memory");

        // Wrap：工具审批/工具结果回合跳过上下文注入，避免破坏工具调用协议要求的消息相邻性
        builder.AddAIContextProvider(ToolContinuationSkippingAiContextProvider.Wrap(new FileMemoryProvider(
            new FileSystemAgentFileStore(memoryPath),
            _ => new FileMemoryState { WorkingFolder = string.Empty },
            new FileMemoryProviderOptions
            {
                Instructions =
                    """
                    ## 基于文件的内存
                    - file_memory_* 仅用于当前会话的工作内存，与其他会话隔离。
                    - 开始新任务前使用 list 和 search 检查已有相关记忆。
                    - 用户明确偏好、约束或纠正必须以简洁中文要点保存。
                    """
            })));
    }
}
#pragma warning restore MAAI001
