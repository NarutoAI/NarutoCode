using Microsoft.Extensions.AI;
using NarutoCode.Domain.Configurations;
using OpenAI.Responses;

namespace NarutoCode.Infrastructure.ChatClients;

/// <summary>
/// OpenAI Responses API 协议的聊天客户端工厂。
/// </summary>
internal sealed class OpenAIResponsesClientFactory : IChatClientFactory
{
    /// <inheritdoc />
    public IChatClient Create(LlmConfiguration configuration)
    {
        var openAIClient = OpenAIClientFactory.Create(configuration);
#pragma warning disable OPENAI001
#pragma warning disable MAAI001
        return openAIClient
            .GetResponsesClient()
            .AsIChatClientWithStoredOutputDisabled(configuration.Model, includeReasoningEncryptedContent: true)
            .AsBuilder()
            .Build();
#pragma warning restore MAAI001
#pragma warning restore OPENAI001
    }
}