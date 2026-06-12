using Microsoft.Extensions.AI;
using NarutoCode.Domain.Configurations;

namespace NarutoCode.Infrastructure.ChatClients;

/// <summary>
/// OpenAI Chat Completions 兼容协议的聊天客户端工厂。
/// </summary>
internal sealed class OpenAIChatClientFactory : IChatClientFactory
{
    public IChatClient Create(LlmConfiguration configuration)
    {
        var openAIClient = OpenAIClientFactory.Create(configuration);
#pragma warning disable OPENAI001
        return openAIClient.GetChatClient(configuration.Model).AsIChatClient();
#pragma warning restore OPENAI001
    }
}