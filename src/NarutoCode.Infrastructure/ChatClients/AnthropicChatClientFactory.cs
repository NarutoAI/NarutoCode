using Anthropic;
using Microsoft.Extensions.AI;
using NarutoCode.Domain.Configurations;

namespace NarutoCode.Infrastructure.ChatClients;

public class AnthropicChatClientFactory : IChatClientFactory
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(15);

    public IChatClient Create(LlmConfiguration configuration)
    {
        //anthropic 客户端aot有问题 ，需要使用此协议的话，不能进行aot发布
        //https://github.com/anthropics/anthropic-sdk-csharp/issues/79
        return new AnthropicClient
        {
            BaseUrl = configuration.Address,
            MaxRetries = 3,
            Timeout = NetworkTimeout,
            ApiKey = configuration.ApiKey,
        }.AsIChatClient(configuration.Model);
    }
}