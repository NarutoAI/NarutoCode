namespace NarutoCode.Domain.Messages;

/// <summary>
/// 保存 Agent 响应期间用户继续输入、等待下一轮统一发送的消息队列。
/// </summary>
public sealed class PendingUserMessageQueue
{
    private readonly Queue<string> _messages = [];
    private readonly Lock _syncRoot = new();
    private string _draft = string.Empty;

    /// <summary>
    /// 当前队列中是否存在待发送消息。
    /// </summary>
    public bool HasMessages
    {
        get
        {
            lock (_syncRoot)
            {
                return _messages.Count > 0;
            }
        }
    }

    /// <summary>
    /// 当前等待期输入草稿。
    /// </summary>
    public string Draft
    {
        get
        {
            lock (_syncRoot)
            {
                return _draft;
            }
        }
    }

    /// <summary>
    /// 创建当前等待发送消息的快照，用于渲染等待期队列。
    /// </summary>
    /// <returns>等待发送消息快照。</returns>
    public IReadOnlyList<string> CreateSnapshot()
    {
        lock (_syncRoot)
        {
            return _messages.ToArray();
        }
    }

    /// <summary>
    /// 更新当前等待期输入草稿。
    /// </summary>
    /// <param name="input">用户正在编辑的输入内容。</param>
    public void UpdateDraft(string input)
    {
        lock (_syncRoot)
        {
            _draft = input;
        }
    }

    /// <summary>
    /// 将一条等待期输入加入队列。
    /// </summary>
    /// <param name="input">用户输入内容。</param>
    public void Enqueue(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        lock (_syncRoot)
        {
            _messages.Enqueue(input);
        }
    }

    /// <summary>
    /// 一次性取出全部等待期输入，并按换行合并为一条消息。
    /// </summary>
    /// <param name="input">合并后的用户消息。</param>
    /// <returns>存在待发送消息时返回 <see langword="true" />。</returns>
    public bool TryDrain(out string input)
    {
        lock (_syncRoot)
        {
            if (_messages.Count == 0)
            {
                input = string.Empty;
                return false;
            }

            input = string.Join(Environment.NewLine, _messages);
            _messages.Clear();
            return true;
        }
    }
}
