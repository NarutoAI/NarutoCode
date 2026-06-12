namespace NarutoCodeCli.Ui;

/// <summary>
/// 定义从系统剪贴板提取图片并保存为工作区相对文件路径的能力。
/// </summary>
internal interface IClipboardImageStore
{
    /// <summary>
    /// 尝试将剪贴板中的一张或多张图片保存为工作区内文件。
    /// </summary>
    /// <param name="relativePaths">保存成功后的工作区相对路径集合。</param>
    /// <returns>至少保存一张图片时返回 <see langword="true" />。</returns>
    bool TrySaveClipboardImages(out IReadOnlyList<string> relativePaths);
}
