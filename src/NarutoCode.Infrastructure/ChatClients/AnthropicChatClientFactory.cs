using Anthropic;
using Microsoft.Extensions.AI;
using NarutoCode.Domain.Configurations;

namespace NarutoCode.Infrastructure.ChatClients;

public class AnthropicChatClientFactory : IChatClientFactory
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(60);

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
            // Handlers 由 SDK 接线到自管 HttpClient；实测 AnthropicClient.Dispose 会连带释放外部传入的
            // HttpClient，因此该链路不走共享实例，通过 Handlers 注入 UA 改写（SDK 负责生命周期）
            Handlers = [new ProductHttpClient.AnthropicUserAgentHandler()]
        }.AsIChatClient(configuration.Model);
    }
}