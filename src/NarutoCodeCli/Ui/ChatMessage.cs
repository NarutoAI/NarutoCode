using System.Text;
using NarutoCode.Domain.Messages;

namespace NarutoCodeCli.Ui;

/// <summary>
/// CLI 聊天消息角色。
/// </summary>
internal enum ChatRole
{
    /// <summary>
    /// 用户输入消息。
    /// </summary>
    User,

    /// <summary>
    /// 助手输出消息。
    /// </summary>
    Assistant
}

/// <summary>
/// CLI 层聊天消息视图模型，仅保存渲染当前画布需要的数据。
/// </summary>
internal sealed class ChatMessage
{
    private readonly List<AgentMessage> agentMessages = [];
    private readonly StringBuilder assistantContent = new();
    private readonly string userContent;
    private string? assistantContentCache;

    private ChatMessage(ChatRole role, string content)
    {
        Role = role;
        userContent = content;
    }

    /// <summary>
    /// 消息角色。
    /// </summary>
    public ChatRole Role { get; }

    /// <summary>
    /// 助手返回的分段消息集合。
    /// </summary>
    public IReadOnlyList<AgentMessage> AgentMessages => agentMessages;

    /// <summary>
    /// 当前消息合并后的文本内容。
    /// </summary>
    public string Content => Role == ChatRole.User
        ? userContent
        : assistantContentCache ??= assistantContent.ToString();

    /// <summary>
    /// 当前消息中模型返回的上下文 Token 用量累计值。
    /// </summary>
    public long ContextTokenUsage => agentMessages
        .Where(message => message.Type == AgentMessageType.Usage)
        .Sum(message => long.TryParse(message.Content, out var usage) ? usage : 0);

    /// <summary>
    /// 当前消息渲染版本，内容变化时递增，用于复用已完成消息的渲染结果。
    /// </summary>
    public int RenderVersion { get; private set; }

    /// <summary>
    /// 创建用户消息。
    /// </summary>
    /// <param name="content">用户输入内容。</param>
    /// <returns>用户消息。</returns>
    public static ChatMessage CreateUser(string content)
    {
        return new ChatMessage(ChatRole.User, content);
    }

    /// <summary>
    /// 创建助手消息。
    /// </summary>
    /// <returns>助手消息。</returns>
    public static ChatMessage CreateAssistant()
    {
        return new ChatMessage(ChatRole.Assistant, string.Empty);
    }

    /// <summary>
    /// 向助手消息追加一段 Agent 返回内容。
    /// </summary>
    /// <param name="message">Agent 返回内容。</param>
    public void Append(AgentMessage message)
    {
        if (string.IsNullOrEmpty(message.Content))
        {
            return;
        }

        if (message.Type == AgentMessageType.Usage)
        {
            agentMessages.Add(message);
            RenderVersion++;
            return;
        }

        if (message.Type != AgentMessageType.ToolApprovalRequest
            && agentMessages.Count > 0
            && agentMessages[^1].Type == message.Type)
        {
            var lastMessage = agentMessages[^1];
            var separator = message.Type is AgentMessageType.ToolCall or AgentMessageType.Error
                ? Environment.NewLine
                : string.Empty;
            var mergedContent = lastMessage.Content + separator + message.Content;

            agentMessages[^1] = new AgentMessage(
                lastMessage.Type,
                mergedContent,
                lastMessage.ToolApprovalContent,
                lastMessage.CreatedAt);
            assistantContent.Append(separator);
            assistantContent.Append(message.Content);
            assistantContentCache = null;
            RenderVersion++;
            return;
        }

        agentMessages.Add(message);
        assistantContent.Append(message.Content);
        assistantContentCache = null;
        RenderVersion++;
    }
}
