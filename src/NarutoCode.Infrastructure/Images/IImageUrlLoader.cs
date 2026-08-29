namespace NarutoCode.Infrastructure.Images;

/// <summary>
/// 图片来源加载器：把本地路径、file:// URL 或 http(s):// URL 统一加载为图片字节。
/// </summary>
public interface IImageUrlLoader
{
    /// <summary>
    /// 加载图片并推断媒体类型。
    /// </summary>
    /// <param name="source">图片来源：纯本地路径、file:// URL 或 http(s):// URL。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>图片字节与媒体类型。</returns>
    /// <exception cref="ArgumentException">来源为空。</exception>
    /// <exception cref="NotSupportedException">协议不支持或无法推断图片 MIME。</exception>
    /// <exception cref="FileNotFoundException">本地文件不存在。</exception>
    /// <exception cref="InvalidOperationException">图片超过大小上限。</exception>
    /// <exception cref="HttpRequestException">HTTP 下载失败。</exception>
    Task<ImageLoadResult> LoadAsync(string source, CancellationToken cancellationToken = default);
}

/// <summary>
/// 图片加载结果：内存字节与推断出的媒体类型（如 image/png）。
/// </summary>
/// <param name="Bytes">图片二进制数据。</param>
/// <param name="MediaType">图片媒体类型。</param>
public sealed record ImageLoadResult(byte[] Bytes, string MediaType);
