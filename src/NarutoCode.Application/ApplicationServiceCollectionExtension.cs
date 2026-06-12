using Microsoft.Extensions.DependencyInjection;
using NarutoCode.Application.Conversations;
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
        }
    }
}