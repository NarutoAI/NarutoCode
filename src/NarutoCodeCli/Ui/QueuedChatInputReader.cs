using System.Text;
using NarutoCode.Domain.Messages;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 在 Agent 运行期间监听用户键盘输入，并将完整行写入等待发送队列。
/// </summary>
internal sealed class QueuedChatInputReader(PendingUserMessageQueue pendingUserMessageQueue)
{
    private const int PollingDelayMilliseconds = 25;

    /// <summary>
    /// 监听等待期输入，直到当前 Agent 请求结束或被取消。
    /// </summary>
    /// <param name="refresh">输入草稿变化后的界面刷新回调。</param>
    /// <param name="cancellationToken">当前 Agent 请求的取消令牌。</param>
    public async Task CaptureAsync(Action refresh, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refresh);

        if (Console.IsInputRedirected)
        {
            return;
        }

        var buffer = new StringBuilder();
        var cursor = 0;
        pendingUserMessageQueue.UpdateDraft(string.Empty);
        refresh();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!Console.KeyAvailable)
                {
                    await Task.Delay(PollingDelayMilliseconds, cancellationToken);
                    continue;
                }

                var key = Console.ReadKey(intercept: true);
                var changed = HandleKey(key, buffer, ref cursor);
                if (!changed)
                {
                    continue;
                }

                pendingUserMessageQueue.UpdateDraft(buffer.ToString());
                refresh();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 当前 Agent 请求结束或被取消时，停止后台输入监听。
        }
        finally
        {
            pendingUserMessageQueue.UpdateDraft(string.Empty);
        }
    }

    private bool HandleKey(ConsoleKeyInfo key, StringBuilder buffer, ref int cursor)
    {
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                pendingUserMessageQueue.Enqueue(buffer.ToString());
                buffer.Clear();
                cursor = 0;
                return true;

            case ConsoleKey.Backspace when cursor > 0:
                buffer.Remove(cursor - 1, 1);
                cursor--;
                return true;

            case ConsoleKey.Delete when cursor < buffer.Length:
                buffer.Remove(cursor, 1);
                return true;

            case ConsoleKey.LeftArrow when cursor > 0:
                cursor--;
                return true;

            case ConsoleKey.RightArrow when cursor < buffer.Length:
                cursor++;
                return true;

            case ConsoleKey.Home:
                cursor = 0;
                return true;

            case ConsoleKey.End:
                cursor = buffer.Length;
                return true;
        }

        if (key.KeyChar == '\0' || char.IsControl(key.KeyChar))
        {
            return false;
        }

        buffer.Insert(cursor, key.KeyChar);
        cursor++;
        return true;
    }
}
