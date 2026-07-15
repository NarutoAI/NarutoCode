namespace NarutoCode.Domain.Conversations;

/// <summary>
/// 打开工作区后的会话结果。
/// </summary>
/// <param name="History">应在界面中打开的会话历史。</param>
/// <param name="Created">是否为工作区创建了首个会话。</param>
public sealed record OpenWorkspaceResult(ConversationHistory History, bool Created);
