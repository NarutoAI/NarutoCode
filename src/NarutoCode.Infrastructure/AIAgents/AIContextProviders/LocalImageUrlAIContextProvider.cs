using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NarutoCode.Infrastructure.Images;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders;

/// <summary>
/// 本地 URL 图片加载提供器：把 file:// 或 http(s):// 图片转成 DataContent，
/// 供当前支持视觉的模型直接查看。仅在模型 SupportsVision=true 时由 Contributor 挂载。
/// </summary>
public sealed class LocalImageUrlAIContextProvider : AIContextProvider
{
    /// <summary>
    /// 工具使用说明，注入到 Agent 提示词。
    /// </summary>
    private const string Instructions =
        """
        ## 本地 URL 图片加载

        你可以使用 `load_local_image_url` 工具把 `file://` 或 `http(s)://` 形式的图片 URL 转成图片内容，供你直接查看。

        使用规则：
        - 输入必须是一个绝对 URL；支持 `file:///path/to/img.png`、`http(s)://host:port/path/img.png`。
        - 仅接受 PNG / JPEG / WebP / GIF，单图不超过 10MB。
        - 工具成功时返回图片内容，你在本轮即可看到图；失败时返回文本错误。
        - 不要在回复中复读图片的每个细节，按用户问题提取关键信息。
        """;

    private readonly IImageUrlLoader _loader;
    private readonly AITool[] _tools;

    /// <summary>
    /// 创建本地 URL 图片加载提供器。
    /// </summary>
    /// <param name="loader">图片来源加载器。</param>
    public LocalImageUrlAIContextProvider(IImageUrlLoader loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _tools =
        [
            AIFunctionFactory.Create(LoadLocalImageUrl, serializerOptions: AIContentJsonSerializerContext.Default.Options)
        ];
    }

    /// <inheritdoc />
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new AIContext
        {
            Instructions = Instructions,
            Tools = _tools
        });
    }

    /// <summary>
    /// 加载 URL 图片并作为多模态内容返回。
    /// </summary>
    /// <param name="imageUrl">图片绝对 URL（file:// 或 http(s)://）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功返回 <see cref="DataContent"/>；失败返回 <see cref="TextContent"/> 错误说明。</returns>
    [Description("把 file:// 或 http(s):// URL 的图片转成图片内容，供当前多模态模型直接查看")]
    private async Task<AIContent> LoadLocalImageUrl(
        [Description("图片绝对 URL，例如 file:///tmp/x.png 或 http://127.0.0.1:3000/screenshot.png")]
        string imageUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return new TextContent("imageUrl 不能为空。");
        }

        try
        {
            var load = await _loader.LoadAsync(imageUrl, cancellationToken).ConfigureAwait(false);
            return new DataContent(load.Bytes, load.MediaType);
        }
        catch (Exception ex)
        {
            // 失败时返回文本错误而非抛异常，避免工具结果里出现堆栈信息
            return new TextContent($"加载图片失败：{ex.Message}");
        }
    }
}
