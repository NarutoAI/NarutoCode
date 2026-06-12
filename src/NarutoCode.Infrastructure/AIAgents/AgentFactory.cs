using System.Runtime.InteropServices;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NarutoCode.Domain;
using NarutoCode.Domain.Workspaces;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;
using NarutoCode.Infrastructure.AIAgents.ChatHistorys;
using NarutoCode.Infrastructure.JsonSerializerContexts;
using NarutoCode.Infrastructure.Tools;

namespace NarutoCode.Infrastructure.AIAgents;

public class AgentFactory(
    IChatClient chatClient,
    IWorkspaceContextAccessor workspaceContextAccessor,
    IChatHistoryPersistenceHandler chatHistoryPersistenceHandler,ILoggerFactory loggerFactory)
    : IAgentFactory, IAsyncDisposable
{
    const int MaxOutputTokens = 128_000;

    /// <summary>
    /// 本地shell工具
    /// </summary>
    private readonly LocalShellExecutor _persistentShell =
        new(new() {Mode = ShellMode.Persistent, AcknowledgeUnsafe = true});

    /// <summary>
    /// 根据不同的类型 创建不同的agent
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public AIAgent Create()
    {
        var skillsProvider =
#pragma warning disable MAAI001
            new AgentSkillsProvider([
                ProjectConstant.SkillsDirectory
            ]);

        //上下文窗口裁剪
        var compactionStrategy = new ContextWindowCompactionStrategy(
            maxContextWindowTokens: AppData.Config.Llm.MaxContextWindowTokens,
            maxOutputTokens: MaxOutputTokens);
        var persistenceChatHistoryProvider = new PersistenceChatHistoryProvider(
            chatHistoryPersistenceHandler,
            compactionStrategy.AsChatReducer());

        var memoryPath = Path.Combine(workspaceContextAccessor.Current.WorkingDirectory, ProjectConstant.ConfigurationDirectory, "memory");
        //校验工作目录是否存在AGENTS.md文档
        var agentMd = AgentsMdAsync(workspaceContextAccessor.Current.WorkingDirectory);
        return chatClient.AsHarnessAgent(AppData.Config.Llm.MaxContextWindowTokens, MaxOutputTokens,
            new HarnessAgentOptions
            {
                AgentModeProviderOptions = new AgentModeProviderOptions
                {
                    Instructions = null,
                    Modes = null,
                    DefaultMode = "execute"
                },
                HarnessInstructions =
                    $"""
                       你是一位强大的软件架构师和产品专家
                       ## 一般指导原则
                       - 在采取行动前，先仔细思考任务。将复杂的工作分解为清晰的步骤。  
                       - 使用可用的工具来收集信息、执行操作并验证结果。  
                       - 在完成任务的过程中，说明你的推理和思维过程。  
                       - 在调用工具之间，解释你所学到的内容以及接下来的计划，以便用户能够跟随你的思路。  
                       - 避免连续调用超过4次工具而未说明当前操作。  
                       - 如果某个工具调用失败或返回了意外结果，应调整方法，而不是重复相同的调用。  
                       - 完成任务后，清晰简洁地总结你所做的工作及发现的结果以及后续所需的工作。
                       
                       ## 个人信息
                       - 姓名：NarutoCode
                       - 作者：Naruto
                       - 作者的Github地址：https://github.com/NarutoAI
                       - 作者的公众号名称：Agent指南针
                       
                      ## 工程规范、
                       - 在编辑文件前务必先阅读其内容，首先理解现有的结构和风格。  
                       - 遵循代码库的约定：命名、格式、模式和惯用法。  
                       - 偏好最小化、精准的修改，而非重写代码。对于现有文件，应使用“编辑”而非“编写”。  
                       - 确保每次更改都完整：添加导入语句、处理错误、遵守类型规范。  
                       - 如果更改涉及公共API或合约，请注明调用者可能需要更新的内容。
                       
                       ## 其它信息
                       - 当前操作系统：`{RuntimeInformation.OSDescription}`
                       ## 重要信息
                       - **除非用户明确要求，否则你必须使用中文回复。**
                       
                       ## 输出风格
                       - 保持简洁明了，尽量减少解释，只有在内容不明显时才进行说明。  
                       - 不要描述代码的具体行为，仅在原因不明确时添加注释。  
                       
                       \n\n
                       {agentMd}
                     """,
                Name = "NarutoCode",
                DisableFileMemory = true,
                ChatHistoryProvider = persistenceChatHistoryProvider,
                ChatOptions = new ChatOptions
                {
                    MaxOutputTokens =
                        MaxOutputTokens,
                    Reasoning = new()
                    {
                        Effort = ReasoningEffort.Medium,
                        Output = ReasoningOutput.Summary,
                    },
                    Tools =
                    [
                        AIFunctionFactory.Create(SearchAgentTools.Glob, serializerOptions: AIContentJsonSerializerContext.Default.Options),
                        AIFunctionFactory.Create(SearchAgentTools.Grep, serializerOptions: AIContentJsonSerializerContext.Default.Options),
                        _persistentShell.AsAIFunction(requireApproval: AppData.Config.EnableApproval)
                    ],
                },
                DisableAgentSkillsProvider = true,
                AIContextProviders =
                [
                    skillsProvider,
                    new TaskProvider(),
                    new SvgRenderProvider(workspaceContextAccessor.Current.WorkingDirectory),
                    new FileAccessProvider(
                        new FileSystemAgentFileStore(workspaceContextAccessor.Current.WorkingDirectory),
                        new FileAccessProviderOptions
                        {
                            Instructions =
                                $"""
                                 ## 文件访问
                                 您可以通过 `FileAccess_*` 工具访问共享的文件存储区域，用于读取、写入和管理文件。  
                                 这些文件在当前会话结束后仍可保留，并可在多个会话或代理之间共享。  
                                 使用这些工具来读取用户提供的输入数据、写入输出结果，以及管理用户要求您处理的任何文件。
                                 - 除非用户明确要求，否则切勿删除或覆盖现有文件。
                                 ## 工作目录地址
                                 - {workspaceContextAccessor.Current.WorkingDirectory}

                                 """
                        }),
                    //记忆
                    new FileMemoryProvider(
                        new FileSystemAgentFileStore(memoryPath),
                        _ => new FileMemoryState
                        {
                            WorkingFolder ="",
                        },new FileMemoryProviderOptions(){
                            Instructions = """
                                           ## 基于文件的内存  
                                           您可以通过 `FileMemory_*` 工具访问一个会话范围内的基于文件的内存系统，用于在交互过程中存储和检索信息。  
                                           这些文件作为当前会话的工作内存，与其他会话相互隔离。  
                                           使用这些工具来存储计划、记忆、处理结果或下载的数据。
                                           - 使用描述性的文件名（例如：“projectarchitecture.md”、“userpreferences.md”）。  
                                           - 保存文件时添加说明，以便日后查找。  
                                           - 开始新任务前，请使用 FileMemory_ListFiles 和 FileMemory_SearchFiles 检查现有相关记忆，避免重复工作。  
                                           - 当信息发生变化时，通过覆盖文件来保持记忆的更新。  
                                           - 当收到大量数据（例如下载的网页、API 响应、研究结果）时，若将来需要使用，请将其保存为文件，以免在压缩或截断较旧上下文时丢失。  
                                           这可确保重要数据在长时间会话中仍能被访问
                                           - 当用户主动输入强调性话语，例如“必须”“一定要”“以后都要”“不要再”等明确偏好或约束时，必须主动提取并调用 `FileMemory_SaveFile` 记住。
                                           - 当用户纠正术语、命名、事实、规则或项目约定时，必须主动调用 `FileMemory_SaveFile` 记住纠正后的信息，并以后以纠正后的信息为准。
                                           - 记忆必须是简洁中文要点，优先归纳而不是复制用户原文。
                                           """
                        }),
                    new CollectApprovalToolAiContextProvider()
                ],
                DisableFileAccess = true, //禁用自带的文件处理
                //文件处理
                // FileAccessStore = new FileSystemAgentFileStore(workspaceContextAccessor.Current.WorkingDirectory),
            },loggerFactory: loggerFactory);
    }


    public async ValueTask DisposeAsync()
    {
        await _persistentShell.DisposeAsync();
        if (chatClient is IAsyncDisposable chatClientAsyncDisposable)
            await chatClientAsyncDisposable.DisposeAsync();
        else
            chatClient.Dispose();
    }

    private static string AgentsMdAsync(string path)
    {
        var agentPath = Path.Combine(path, "AGENTS.md");
        if (!File.Exists(agentPath))
        {
            return string.Empty;
        }

        return $"## 项目信息 \n {File.ReadAllText(agentPath)}";
    }
}