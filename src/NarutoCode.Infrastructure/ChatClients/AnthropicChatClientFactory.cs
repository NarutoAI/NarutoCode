using Anthropic;
using Microsoft.Extensions.AI;
using NarutoCode.Domain.Configurations;

namespace NarutoCode.Infrastructure.ChatClients;

public class AnthropicChatClientFactory : IChatClientFactory
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(15);

    public IChatClient Create(LlmConfiguration configuration)
    {
        return new AnthropicClient
        {
            BaseUrl = configuration.Address,
            MaxRetries = 3,
            Timeout = NetworkTimeout,
            ApiKey = configuration.ApiKey,
        }.AsIChatClient(configuration.Model);
    }
}