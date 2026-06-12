namespace NarutoCodeCli.Ui;

/// <summary>
/// 读取会话入口页的键盘操作。
/// </summary>
internal sealed class SessionLauncherPromptReader
{
    /// <summary>
    /// 读取一个会话入口页按键。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>读取到的按键。</returns>
    public async Task<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (Console.IsInputRedirected)
            {
                return new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
            }

            if (Console.KeyAvailable)
            {
                return Console.ReadKey(intercept: true);
            }

            await Task.Delay(25, cancellationToken);
        }

        return new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false);
    }
}
