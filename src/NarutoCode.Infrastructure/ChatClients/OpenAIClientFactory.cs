using System.ClientModel;
using NarutoCode.Domain.Configurations;
using OpenAI;

namespace NarutoCode.Infrastructure.ChatClients;

internal static class OpenAIClientFactory
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(15);

    public static OpenAIClient Create(LlmConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.Address))
        {
            throw new InvalidOperationException("模型地址未填写。");
        }

        if (!Uri.TryCreate(configuration.Address, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("模型地址必须是有效的绝对地址。");
        }

        if (string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            throw new InvalidOperationException("模型 ApiKey 未填写。");
        }

        if (string.IsNullOrWhiteSpace(configuration.Model))
        {
            throw new InvalidOperationException("模型名称未填写。");
        }

        return new OpenAIClient(
            new ApiKeyCredential(configuration.ApiKey),
            new OpenAIClientOptions
            {
                Endpoint = endpoint,
                NetworkTimeout = NetworkTimeout
            });
    }
}
