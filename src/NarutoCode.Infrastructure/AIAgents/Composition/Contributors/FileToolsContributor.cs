#pragma warning disable MAAI001
using Microsoft.Agents.AI;
using NarutoCode.Domain.Workspaces;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;
using NarutoCode.Infrastructure.AIAgents.DelegatingChatClients;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// 文件工具贡献者：挂载代码审核、文件系统工具与文件访问提供器；
/// FileAccessProvider 实例在 CodeReview 审核器与主 Agent 工具列表间共享。
/// </summary>
public sealed class FileToolsContributor(DynamicChatClient dynamicChatClient) : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "FileTools";

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        // FileAccessProvider 实例共享：CodeReview 审核器与主 Agent 工具列表必须引用同一实例
        var fileAccessProvider = new FileAccessProvider(
            new FileSystemAgentFileStore(context.WorkingDirectory),
            new FileAccessProviderOptions
            {
                Instructions =
                    """
                    ## 文件访问
                    您可以通过 `file_access_*` 工具访问当前工作目录中的文件。
                    - 除非用户明确要求，否则切勿删除或覆盖现有文件。
                    - `fileName` 或 `directory` 参数必须使用相对工作目录的路径。

                    ## 使用 `edit_file` 工具规则
                    - 如果 `old_string` 在文件中不唯一，则编辑会失败。请提供更长的上下文，或使用 `replace_all`。
                    """
            });

        builder.AddAIContextProvider(new CodeReviewAIContextProvider(dynamicChatClient, [fileAccessProvider]));
        builder.AddAIContextProvider(new FSTollsAiContextProvider(
            new FixedWorkspaceContextAccessor(new WorkspaceContext(context.WorkingDirectory))));
        builder.AddAIContextProvider(fileAccessProvider);
    }
}
#pragma warning restore MAAI001
