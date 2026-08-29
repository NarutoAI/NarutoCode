using Anthropic;
using System.ClientModel;
using Microsoft.Extensions.AI;
using NarutoCode.Domain.Configurations;
using NarutoCode.Domain.Enums;
using OpenAI;
using OpenAI.Responses;

namespace NarutoCode.Infrastructure.Vision;

/// <summary>
/// 独立视觉模型客户端：把图片字节与提示词发送给 OpenAI 兼容的多模态端点，返回文本描述。
/// 独立于主 LLM 的 keyed IChatClient 管道（独立 endpoint / ApiKey / 超时）。
/// </summary>
public sealed class VisionChatClient : IVisionChatClient
{
    private const string SystemPrompt =
        """
        你是一个负责为其他语言模型转述图片内容的视觉解析助手。请准确、客观地描述图片中的关键信息，提取所有清晰可读的文字（OCR），并说明界面元素、布局、状态、错误信息和与用户请求相关的内容。不要臆测图片中不存在的信息；无法确认时请明确说明。输出纯文本，不要使用 Markdown 图片语法，也不要声称自己无法查看图片。
        """;

    private readonly VisionConfiguration _configuration;

    /// <summary>
    /// 创建视觉模型客户端。
    /// </summary>
    /// <param name="configuration">视觉模型配置，Address/ApiKey/Model 必须已校验非空。</param>
    public VisionChatClient(VisionConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// 识别单张图片并返回自然语言描述。
    /// </summary>
    /// <param name="image">图片二进制数据。</param>
    /// <param name="mediaType">图片媒体类型（如 image/png）。</param>
    /// <param name="prompt">识别指令，驱动视觉模型的输出方向。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>视觉模型输出的文本描述。</returns>
    public async Task<string> RecognizeAsync(
        byte[] image,
        string mediaType,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        // 按视觉模型协议创建独立客户端，不复用主 LLM 的 endpoint、密钥和超时配置。
        using var chatClient = CreateChatClient();

        // 系统消息约束视觉模型只输出可供主模型消费的客观文本；用户消息保留调用方指定的识别方向与图片数据。
        var systemMessage = new ChatMessage(ChatRole.System, SystemPrompt);
        var userMessage = new ChatMessage(ChatRole.User,
        [
            new TextContent(prompt),
            new DataContent(image, mediaType)
        ]);

        var response = await chatClient.GetResponseAsync(
            [systemMessage, userMessage],
            new ChatOptions { MaxOutputTokens = _configuration.MaxOutputTokens },
            cancellationToken).ConfigureAwait(false);
        return response.Text;
    }

    /// <summary>
    /// 根据独立视觉模型配置创建对应协议的多模态聊天客户端。
    /// </summary>
    /// <returns>用于单次图片识别请求的聊天客户端。</returns>
    /// <exception cref="InvalidOperationException">配置的协议不受支持时抛出。</exception>
    private IChatClient CreateChatClient()
    {
        if (!Enum.TryParse<LlmProtocol>(_configuration.Protocol, ignoreCase: true, out var protocol))
        {
            throw new InvalidOperationException(
                $"视觉模型协议 {_configuration.Protocol} 不受支持，仅支持 OpenAIChat、OpenAIResponses 或 Anthropic。");
        }

        return protocol switch
        {
            LlmProtocol.OpenAIChat => CreateOpenAIChatClient(),
            LlmProtocol.OpenAIResponses => CreateOpenAIResponsesClient(),
            LlmProtocol.Anthropic => new AnthropicClient
            {
                BaseUrl = _configuration.Address,
                MaxRetries = 3,
                Timeout = TimeSpan.FromSeconds(_configuration.TimeoutSeconds),
                ApiKey = _configuration.ApiKey
            }.AsIChatClient(_configuration.Model),
            _ => throw new InvalidOperationException(
                $"视觉模型协议 {_configuration.Protocol} 不受支持，仅支持 OpenAIChat、OpenAIResponses 或 Anthropic。")
        };
    }

    /// <summary>
    /// 创建 OpenAI Chat Completions 协议的独立客户端。
    /// </summary>
    /// <returns>用于 OpenAI Chat Completions 协议的聊天客户端。</returns>
    private IChatClient CreateOpenAIChatClient()
    {
#pragma warning disable OPENAI001
        return CreateOpenAIClient().GetChatClient(_configuration.Model).AsIChatClient();
#pragma warning restore OPENAI001
    }

    /// <summary>
    /// 创建 OpenAI Responses 协议的独立客户端。
    /// </summary>
    /// <returns>用于 OpenAI Responses 协议的聊天客户端。</returns>
    private IChatClient CreateOpenAIResponsesClient()
    {
#pragma warning disable OPENAI001
#pragma warning disable MAAI001
        return CreateOpenAIClient()
            .GetResponsesClient()
            .AsIChatClientWithStoredOutputDisabled(_configuration.Model, includeReasoningEncryptedContent: true)
            .AsBuilder()
            .Build();
#pragma warning restore MAAI001
#pragma warning restore OPENAI001
    }

    /// <summary>
    /// 创建 OpenAI Chat 或 Responses 协议所需的独立客户端。
    /// </summary>
    /// <returns>已应用独立视觉模型地址、密钥和超时配置的 OpenAI 客户端。</returns>
    private OpenAIClient CreateOpenAIClient()
    {
        return new OpenAIClient(
            new ApiKeyCredential(_configuration.ApiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(_configuration.Address),
                NetworkTimeout = TimeSpan.FromSeconds(_configuration.TimeoutSeconds)
            });
    }
}
