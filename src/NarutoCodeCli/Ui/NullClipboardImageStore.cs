namespace NarutoCodeCli.Ui;

/// <summary>
/// 表示当前平台不支持剪贴板图片读取时的空实现。
/// </summary>
internal sealed class NullClipboardImageStore : IClipboardImageStore
{
    /// <inheritdoc />
    public bool TrySaveClipboardImages(out IReadOnlyList<string> relativePaths)
    {
        relativePaths = [];
        return false;
    }
}
