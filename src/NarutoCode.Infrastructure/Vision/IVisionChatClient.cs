namespace NarutoCode.Infrastructure.Vision;

/// <summary>
/// 独立视觉模型客户端：把图片字节与提示词发送给视觉模型，返回文本描述。
/// </summary>
public interface IVisionChatClient
{
    /// <summary>
    /// 识别单张图片并返回自然语言描述。
    /// </summary>
    /// <param name="image">图片二进制数据。</param>
    /// <param name="mediaType">图片媒体类型（如 image/png）。</param>
    /// <param name="prompt">识别指令，驱动视觉模型的输出方向。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>视觉模型输出的文本描述。</returns>
    Task<string> RecognizeAsync(
        byte[] image,
        string mediaType,
        string prompt,
        CancellationToken cancellationToken = default);
}
