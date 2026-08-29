using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NarutoCode.Application;
using NarutoCode.Application.Agents;
using NarutoCode.Domain;
using NarutoCode.Domain.Configurations;
using NarutoCode.Domain.Configurations.Settings;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Enums;
using NarutoCode.Domain.Interactions;
using NarutoCode.Domain.LlmContextAccessors;
using NarutoCode.Infrastructure.AIAgents;
using NarutoCode.Infrastructure.AIAgents.ChatHistorys;
using NarutoCode.Infrastructure.AIAgents.Composition;
using NarutoCode.Infrastructure.AIAgents.Composition.Contributors;
using NarutoCode.Infrastructure.AIAgents.CompactionStrategys;
using NarutoCode.Infrastructure.AIAgents.DelegatingChatClients;
using NarutoCode.Infrastructure.AIAgents.Mcp;
using NarutoCode.Infrastructure.AIAgents.SubAgents;
using NarutoCode.Infrastructure.ChatClients;
using NarutoCode.Infrastructure.Images;
using NarutoCode.Infrastructure.Stores;

namespace NarutoCode.Infrastructure;

/// <summary>
/// 基础设施层依赖注入注册入口。
/// </summary>
public static class InfrastructureServiceCollectionExtension
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// 注册基础设施层服务，包括应用层依赖、LLM 协议工厂和当前配置对应的聊天客户端。
        /// </summary>
        /// <param name="logFileName">日志文件名前缀，CLI 保持默认 <c>.log</c>，桌面端传 <c>desktop-api-.log</c>。</param>
        /// <param name="enableUserInteractionTools">是否启用 ask_user 用户交互工具；仅 CLI 传 true，桌面端/网关保持默认 false。</param>
        public async Task AddInfrastructure(string logFileName = ".log", bool enableUserInteractionTools = false)
        {
            await services.AddApplication();

            services.AddKeyedSingleton<IChatClientFactory, OpenAIChatClientFactory>(nameof(LlmProtocol.OpenAIChat));
            services.AddKeyedSingleton<IChatClientFactory, OpenAIResponsesClientFactory>(
                nameof(LlmProtocol.OpenAIResponses));
            services.AddKeyedSingleton<IChatClientFactory, AnthropicChatClientFactory>(nameof(LlmProtocol.Anthropic));

            //注册动态聊天客户端
            services.AddSingleton<DynamicChatClient>();
            services.AddSingleton<IImageUrlLoader, ImageUrlLoader>();
            services.AddSingleton<CompactionStrategyCoordinator>();
            services.AddSingleton<ILlmContextAccessor, LlmContextAccessor>();
            services.AddSingleton<ILlmSettingsService, LlmSettingsService>();
            foreach (var llm in AppData.Config.Llms)
            {
                services.AddKeyedSingleton<IChatClient>(llm.Provider, (provider, _) =>
                    provider.GetRequiredKeyedService<IChatClientFactory>(llm.Protocol)
                        .Create(llm)
                        .AsBuilder()
                        .UseListeningMessageQueue()
                        // 传输层流式重试放在最内层（后注册先包裹内层）：重试只重发底层 HTTP 请求，不重放上层管道副作用
                        .UseStreamingRetry()
                        .Build());
            }

            services.AddSingleton<IAgentChatClient, MafAgentChatClient>();
            // 用户交互工具开关：决定会话级 Agent 是否挂载 ask_user_* 工具（仅 CLI 启用）
            services.AddSingleton(new AgentFactoryOptions(enableUserInteractionTools));

            // Agent 编排贡献者：注册顺序即装配顺序
            services.AddSingleton<IAgentContributor, CoreInstructionsContributor>();
            services.AddSingleton<IAgentContributor, AgentModeContributor>();
            services.AddSingleton<IAgentContributor, ChatHistoryContributor>();
            services.AddSingleton<IAgentContributor, SkillsContributor>();
            services.AddSingleton<IAgentContributor, ShellToolContributor>();
            services.AddSingleton<IAgentContributor, LocalCodeActContributor>();
            services.AddSingleton<IAgentContributor, TaskProviderContributor>();
            services.AddSingleton<IAgentContributor, FileToolsContributor>();
            services.AddSingleton<IAgentContributor, SvgRenderContributor>();
            services.AddSingleton<IAgentContributor, LocalImageUrlContributor>();
            services.AddSingleton<IAgentContributor, FileMemoryContributor>();
            services.AddSingleton<IAgentContributor, TodoContributor>();
            services.AddSingleton<IAgentContributor, McpToolsContributor>();
            services.AddSingleton<IAgentContributor, SubAgentDelegationContributor>();
            services.AddSingleton<IAgentContributor, CollectApprovalContributor>();
            services.AddSingleton<IAgentContributor, UserInteractionContributor>();
            services.AddSingleton<IAgentContributor, LoopEvaluatorContributor>();
            services.AddSingleton<AgentComposer>();
            services.AddSingleton<ConversationRuntimeCache>();
            services.AddSingleton<IAgentFactory, AgentFactory>();

            // 子 Agent 编排：加载配置注册表并注册工作目录执行锁
            var subAgentRegistry = new SubAgentRegistry(
                Path.Combine(ProjectConstant.AppDirectory, ProjectConstant.SubAgentsConfigurationFileName));
            await subAgentRegistry.InitializeAsync();
            services.AddSingleton(subAgentRegistry);
            services.AddSingleton<McpClientManager>();
            services.AddSingleton<ConversationRepositoryCoordinator>();
            services.AddSingleton<IChatHistoryPersistenceHandler, ConversationChatHistoryPersistenceHandler>();
            services.AddSingleton<IConversationRepository, ConversationRepository>();
            services.AddSingleton<IUserInteractionStore, UserInteractionRepository>();
            services.AddSingleton<DbInitializer>();
            services.AddLogging();
            services.AddLogger(logFileName);

            var dataDirectory = Path.Combine(ProjectConstant.AppDirectory, ProjectConstant.DataDirectory);
            if (!Directory.Exists(dataDirectory))
            {
                Directory.CreateDirectory(dataDirectory);
            }

            var databasePath = Path.Combine(dataDirectory, "data.db");
            services.AddSingleton(new SqliteConnectionFactory(databasePath));
        }
    }

    extension(IServiceProvider serviceProvider)
    {
        public async Task BuildAsync()
        {
            //数据库初始化
            await serviceProvider.GetRequiredService<DbInitializer>().InitializeAsync();
            serviceProvider.GetRequiredService<ILlmSettingsService>();
            RootServiceProviderLocator.Init(serviceProvider);
        }
    }
}