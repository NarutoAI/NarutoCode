using System.ClientModel;
using Microsoft.Extensions.AI;
using NarutoCode.Domain.Configurations;
using OpenAI;

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
        // 视觉调用频次低，按配置构造一次性客户端，不复用主 LLM 连接管道
        var openAIClient = new OpenAIClient(
            new ApiKeyCredential(_configuration.ApiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(_configuration.Address),
                NetworkTimeout = TimeSpan.FromSeconds(_configuration.TimeoutSeconds)
            });
#pragma warning disable OPENAI001
       using var chatClient = openAIClient.GetChatClient(_configuration.Model).AsIChatClient();
#pragma warning restore OPENAI001

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
}
