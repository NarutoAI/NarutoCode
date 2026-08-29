using System.ComponentModel;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NarutoCode.Domain;
using NarutoCode.Domain.Configurations.Settings;
using NarutoCode.Infrastructure.Images;
using NarutoCode.Infrastructure.JsonSerializerContexts;
using NarutoCode.Infrastructure.Vision;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders;

/// <summary>
/// 图片识别上下文提供器：主模型不支持视觉时，调用独立配置的小视觉模型识别图片并返回文本描述。
/// 仅在 VisionConfiguration 配置完整时由 Contributor 挂载。
/// </summary>
public sealed class VisionAIContextProvider : AIContextProvider
{
    /// <summary>
    /// 工具使用说明，注入到 Agent 提示词。
    /// </summary>
    private const string Instructions =
        """
        ## 图片识别

        你可以使用 `recognize_image` 工具调用独立视觉模型识别单张图片，返回自然语言文本描述。当前主模型可能不支持视觉输入，该工具是查看图片内容的唯一途径。

        使用规则：
        - `imagePath` 与 `imageUrl` 必须二选一，另一个留空；两者都传或都为空会返回错误。
        - `imagePath` 接受本地文件路径；`imageUrl` 支持 `http(s)://` 与 `file://`。
        - 仅支持 PNG / JPEG / WebP / GIF，单图不超过 10MB。
        - `prompt` 必填，用中文明确识别方向，例如「提取图中所有文字并保留版式」「描述这张 UI 截图的布局与可见错误」。
        - 工具返回的描述是视觉模型的输出，作为你回答用户的依据；不要凭空补充图中不存在的内容。
        """;

    private readonly IImageUrlLoader _loader;
    private readonly IVisionChatClient _client;
    private readonly AITool[] _tools;
    private readonly ILlmSettingsService _llmSettingsService;

    /// <summary>
    /// 创建图片识别上下文提供器。
    /// </summary>
    /// <param name="loader">图片来源加载器，统一处理本地路径与 URL。</param>
    /// <param name="client">独立视觉模型客户端。</param>
    /// <param name="llmSettingsService">当前主模型设置服务，用于判断是否需要独立视觉能力。</param>
    public VisionAIContextProvider(
        IImageUrlLoader loader,
        IVisionChatClient client,
        ILlmSettingsService llmSettingsService)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _llmSettingsService = llmSettingsService ?? throw new ArgumentNullException(nameof(llmSettingsService));
        _tools =
        [
            AIFunctionFactory.Create(RecognizeImage, name: "recognize_image",
                serializerOptions: AIContentJsonSerializerContext.Default.Options)
        ];
    }

    /// <inheritdoc />
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        // 仅纯文本主模型需要借助独立视觉模型；未配置或配置无效时不向 Agent 暴露该工具。
        if (_llmSettingsService.CurrentLlm.SupportsVision || AppData.Config.Vision?.IsValid != true)
        {
            return ValueTask.FromResult(new AIContext());
        }

        return ValueTask.FromResult(new AIContext
        {
            Instructions = Instructions,
            Tools = _tools
        });
    }

    /// <summary>
    /// 调用独立视觉模型识别单张图片，返回结构化 JSON 工具结果。
    /// </summary>
    /// <param name="imagePath">图片本地路径；与 imageUrl 二选一。</param>
    /// <param name="imageUrl">图片 http(s):// 或 file:// URL；与 imagePath 二选一。</param>
    /// <param name="prompt">中文识别指令，例如：提取图中所有文字。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>结构化 JSON 工具结果，成功时包含 description 字段。</returns>
    [Description("调用独立视觉模型识别单张图片，返回文本描述；imagePath 与 imageUrl 二选一")]
    internal async Task<string> RecognizeImage(
        [Description("图片本地路径，例如 tmp/screenshot.png；与 imageUrl 二选一")]
        string? imagePath = null,
        [Description("图片 http(s):// 或 file:// URL；与 imagePath 二选一")]
        string? imageUrl = null,
        [Description("中文识别指令，例如：提取图中所有文字并保留版式")]
        string prompt = "",
        CancellationToken cancellationToken = default)
    {
        // 互斥校验：两者都传或都为空均视为参数错误
        var hasPath = !string.IsNullOrWhiteSpace(imagePath);
        var hasUrl = !string.IsNullOrWhiteSpace(imageUrl);
        if (hasPath == hasUrl)
        {
            return Serialize(new VisionRecognitionToolResult
            {
                Success = false,
                Error = "imagePath 与 imageUrl 必须二选一：只传其中一个，另一个留空。"
            });
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return Serialize(new VisionRecognitionToolResult
            {
                Success = false,
                Error = "prompt 不能为空，请用中文描述需要从图片中识别的内容。"
            });
        }

        var source = hasPath ? imagePath!.Trim() : imageUrl!.Trim();

        // 加载图片字节：复用统一加载器（本地路径 / file:// / http(s):// 分流 + 大小与 MIME 校验）
        ImageLoadResult load;
        try
        {
            load = await _loader.LoadAsync(source, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Serialize(new VisionRecognitionToolResult
            {
                Success = false,
                Source = source,
                Error = $"图片加载失败：{ex.Message}"
            });
        }

        // 调用独立视觉模型识别
        try
        {
            var description = await _client
                .RecognizeAsync(load.Bytes, load.MediaType, prompt, cancellationToken)
                .ConfigureAwait(false);
            return Serialize(new VisionRecognitionToolResult
            {
                Success = true,
                Description = description,
                Source = source,
                MediaType = load.MediaType,
                Bytes = load.Bytes.Length
            });
        }
        catch (Exception ex)
        {
            return Serialize(new VisionRecognitionToolResult
            {
                Success = false,
                Source = source,
                MediaType = load.MediaType,
                Bytes = load.Bytes.Length,
                Error = $"视觉模型调用失败：{ex.Message}"
            });
        }
    }

    /// <summary>
    /// 序列化工具结果为 JSON 字符串。
    /// </summary>
    private static string Serialize(VisionRecognitionToolResult value)
    {
        return JsonSerializer.Serialize(value, AIContentJsonSerializerContext.Default.VisionRecognitionToolResult);
    }
}
