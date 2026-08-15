using Microsoft.Extensions.DependencyInjection;
using NarutoCode.Application.Conversations;
using NarutoCode.Application.Interactions;
using NarutoCode.Domain;
using NarutoCode.Domain.Conversations;

namespace NarutoCode.Application;

public static class ApplicationServiceCollectionExtension
{
    extension(IServiceCollection services)
    {
        public async Task AddApplication()
        {
            await AppData.InitAsync();
            services.AddSingleton<IConversationService, ConversationService>();
            // 用户交互管理器：依赖的 IUserInteractionStore 由 Infrastructure 层注册，单例惰性解析
            services.AddSingleton<IUserInteractionManager, UserInteractionManager>();
        }
    }
}