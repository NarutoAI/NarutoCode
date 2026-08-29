namespace NarutoCode.Infrastructure.Images;

/// <summary>
/// 图片来源加载器默认实现：分流 file:// / http(s):// / 纯本地路径，统一大小与 MIME 校验。
/// </summary>
public sealed class ImageUrlLoader : IImageUrlLoader
{
    /// <summary>
    /// HTTP 下载超时时间。
    /// </summary>
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 可注入的 HttpClient 构造工厂；生产环境为 null（内部新建），单元测试注入 mock handler。
    /// </summary>
    private readonly Func<HttpMessageHandler>? _handlerFactory;

    /// <summary>
    /// 创建默认加载器。
    /// </summary>
    public ImageUrlLoader()
    {
    }

    /// <summary>
    /// 创建使用指定 handler 工厂的加载器（单元测试用）。
    /// </summary>
    /// <param name="handlerFactory">每次下载时构造 HttpMessageHandler 的工厂。</param>
    internal ImageUrlLoader(Func<HttpMessageHandler> handlerFactory)
    {
        _handlerFactory = handlerFactory;
    }

    /// <inheritdoc />
    public async Task<ImageLoadResult> LoadAsync(string source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("图片来源不能为空。", nameof(source));
        }

        // file:// URL：转本地路径走文件读取
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeFile)
        {
            return await LoadFromLocalFileAsync(uri.LocalPath, cancellationToken).ConfigureAwait(false);
        }

        // http(s):// URL：走 HTTP 下载
        if (Uri.TryCreate(source, UriKind.Absolute, out uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return await LoadFromHttpAsync(uri, cancellationToken).ConfigureAwait(false);
        }

        // 兜底：不含协议分隔符的字符串按纯本地路径处理
        if (!source.Contains("://", StringComparison.Ordinal))
        {
            return await LoadFromLocalFileAsync(source, cancellationToken).ConfigureAwait(false);
        }

        throw new NotSupportedException($"不支持的图片来源协议：{source}");
    }

    /// <summary>
    /// 从本地文件读取图片字节并按扩展名推断 MIME。
    /// </summary>
    /// <param name="path">本地文件路径。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>图片字节与媒体类型。</returns>
    private static async Task<ImageLoadResult> LoadFromLocalFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"图片文件不存在：{path}", path);
        }

        // 读字节前先用文件长度快速失败，避免大文件读入内存后才拒绝
        var info = new FileInfo(path);
        if (info.Length > VisionLimits.MaxImageBytes)
        {
            throw new InvalidOperationException(
                $"图片超过 {VisionLimits.MaxImageBytes / 1024 / 1024}MB 限制（{info.Length} 字节）。");
        }

        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        var mediaType = VisionLimits.InferMediaTypeFromExtension(path)
            ?? throw new NotSupportedException($"无法从扩展名推断图片 MIME：{Path.GetExtension(path)}");
        return new ImageLoadResult(bytes, mediaType);
    }

    /// <summary>
    /// 通过 HTTP 下载图片；Content-Type 优先推断 MIME，扩展名兜底。
    /// </summary>
    /// <param name="uri">目标 URL。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>图片字节与媒体类型。</returns>
    private async Task<ImageLoadResult> LoadFromHttpAsync(Uri uri, CancellationToken ct)
    {
        // 每次下载新建 HttpClient：视觉调用频次低，避免引入 IHttpClientFactory 依赖；测试时走注入的 handler
        using var http = CreateHttpClient();
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // 优先从 Content-Type 推断，缺失或非图片时用 URL 扩展名兜底
        var contentType = response.Content.Headers.ContentType?.MediaType;
        var mediaType = VisionLimits.InferMediaTypeFromContentType(contentType)
            ?? VisionLimits.InferMediaTypeFromExtension(uri.AbsolutePath)
            ?? throw new NotSupportedException($"无法识别图片 MIME：Content-Type={contentType ?? "null"}");

        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (bytes.Length > VisionLimits.MaxImageBytes)
        {
            throw new InvalidOperationException(
                $"图片超过 {VisionLimits.MaxImageBytes / 1024 / 1024}MB 限制（{bytes.Length} 字节）。");
        }

        return new ImageLoadResult(bytes, mediaType);
    }

    /// <summary>
    /// 构造 HttpClient：生产用默认 handler，测试用注入工厂。
    /// </summary>
    private HttpClient CreateHttpClient()
    {
        return _handlerFactory is null
            ? new HttpClient { Timeout = HttpTimeout }
            : new HttpClient(_handlerFactory(), disposeHandler: true) { Timeout = HttpTimeout };
    }
}
