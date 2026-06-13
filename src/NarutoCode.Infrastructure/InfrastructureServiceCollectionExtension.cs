using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NarutoCode.Application;
using NarutoCode.Application.Agents;
using NarutoCode.Domain;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Enums;
using NarutoCode.Infrastructure.AIAgents;
using NarutoCode.Infrastructure.AIAgents.ChatHistorys;
using NarutoCode.Infrastructure.AIAgents.DelegatingChatClients;
using NarutoCode.Infrastructure.ChatClients;
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
        public async Task AddInfrastructure()
        {
            await services.AddApplication();

            services.AddKeyedSingleton<IChatClientFactory, OpenAIChatClientFactory>(nameof(LlmProtocol.OpenAIChat));
            services.AddKeyedSingleton<IChatClientFactory, OpenAIResponsesClientFactory>(
                nameof(LlmProtocol.OpenAIResponses));
            services.AddKeyedSingleton<IChatClientFactory, AnthropicChatClientFactory>(nameof(LlmProtocol.Anthropic));
            services.AddSingleton<IChatClient>(provider =>
                provider.GetRequiredKeyedService<IChatClientFactory>(AppData.Config.Llm.Protocol)
                    .Create(AppData.Config.Llm)
                    .AsBuilder()
                    .UseListeningMessageQueue()
                    .Build()
                );
            services.AddSingleton<IAgentChatClient, MafAgentChatClient>();
            services.AddSingleton<IAgentFactory, AgentFactory>();
            services.AddSingleton<ConversationRepositoryCoordinator>();
            services.AddSingleton<IChatHistoryPersistenceHandler, ConversationChatHistoryPersistenceHandler>();
            services.AddSingleton<IConversationRepository, ConversationRepository>();
            services.AddSingleton<DbInitializer>();
            services.AddLogging();
            services.AddLogger();

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
            RootServiceProviderLocator.Init(serviceProvider);
        }
    }
}