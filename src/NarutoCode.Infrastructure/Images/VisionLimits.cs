namespace NarutoCode.Infrastructure.Images;

/// <summary>
/// 视觉相关共享限制与 MIME 推断工具。
/// 供 <see cref="ImageUrlLoader"/> 与视觉类 Provider 统一复用，调整上限只需改这一处。
/// </summary>
public static class VisionLimits
{
    /// <summary>
    /// 单张图片最大字节数（10MB），超过即拒绝加载。
    /// </summary>
    public const long MaxImageBytes = 10 * 1024 * 1024;

    /// <summary>
    /// 允许的图片媒体类型白名单（大小写不敏感）。
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedMediaTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/png",
            "image/jpeg",
            "image/webp",
            "image/gif",
        };

    /// <summary>
    /// 按文件扩展名推断图片媒体类型。
    /// </summary>
    /// <param name="path">文件路径或 URL 路径。</param>
    /// <returns>命中的媒体类型；不在白名单时返回 <see langword="null" />。</returns>
    public static string? InferMediaTypeFromExtension(string path)
    {
        // 统一小写后按扩展名映射，未知扩展名返回 null 交由调用方兜底
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => null,
        };
    }

    /// <summary>
    /// 从 HTTP Content-Type 头推断图片媒体类型。
    /// </summary>
    /// <param name="contentType">Content-Type 值，可能带 <c>; charset=...</c> 参数。</param>
    /// <returns>命中的媒体类型；为空或不在白名单时返回 <see langword="null" />。</returns>
    public static string? InferMediaTypeFromContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        // 去掉 "; charset=..." 等参数部分，只保留主类型
        var mediaType = contentType.Split(';', 2)[0].Trim();
        return AllowedMediaTypes.Contains(mediaType) ? mediaType : null;
    }
}
