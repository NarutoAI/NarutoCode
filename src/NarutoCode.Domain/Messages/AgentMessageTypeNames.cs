namespace NarutoCode.Domain.Messages;

/// <summary>
/// Agent 消息类型与持久化 snake_case 文本的映射。
/// <see cref="AgentMessageType"/> 枚举仅用于内存流转与 TUI 渲染判定，
/// 数据库（agent_chat_messages.type 与 agent_session_items.payload）统一存储本类定义的文本值。
/// </summary>
public static class AgentMessageTypeNames
{
    /// <summary>普通文本消息。</summary>
    public const string Content = "content";

    /// <summary>思考（推理过程）消息。</summary>
    public const string Thinking = "thinking";

    /// <summary>工具调用消息。</summary>
    public const string ToolCall = "tool_call";

    /// <summary>工具审批请求。</summary>
    public const string ToolApprovalRequest = "tool_approval_request";

    /// <summary>工具审批响应。</summary>
    public const string ToolApprovalResponse = "tool_approval_response";

    /// <summary>框架临时注入的上下文消息，不进 UI 历史也不恢复到 Agent 上下文。</summary>
    public const string Temporary = "temporary";

    /// <summary>错误消息。</summary>
    public const string Error = "error";

    /// <summary>
    /// 将枚举转换为持久化文本。
    /// Plan/RemainingTask/Usage 仅在内存中流转、不落库，防御性回退为 content。
    /// </summary>
    /// <param name="type">消息类型枚举。</param>
    /// <returns>snake_case 持久化文本。</returns>
    public static string ToName(AgentMessageType type) => type switch
    {
        AgentMessageType.Content => Content,
        AgentMessageType.Thinking => Thinking,
        AgentMessageType.ToolCall => ToolCall,
        AgentMessageType.ToolApprovalRequest => ToolApprovalRequest,
        AgentMessageType.ToolApprovalResponse => ToolApprovalResponse,
        AgentMessageType.Temporary => Temporary,
        AgentMessageType.Error => Error,
        _ => Content
    };

    /// <summary>
    /// 尝试将持久化文本解析回枚举。
    /// </summary>
    /// <param name="name">持久化文本。</param>
    /// <param name="type">解析结果；无法识别时为 <see cref="AgentMessageType.Content"/>。</param>
    /// <returns>识别成功返回 <see langword="true" />。</returns>
    public static bool TryParse(string? name, out AgentMessageType type)
    {
        switch (name)
        {
            case Content:
                type = AgentMessageType.Content;
                return true;
            case Thinking:
                type = AgentMessageType.Thinking;
                return true;
            case ToolCall:
                type = AgentMessageType.ToolCall;
                return true;
            case ToolApprovalRequest:
                type = AgentMessageType.ToolApprovalRequest;
                return true;
            case ToolApprovalResponse:
                type = AgentMessageType.ToolApprovalResponse;
                return true;
            case Temporary:
                type = AgentMessageType.Temporary;
                return true;
            case Error:
                type = AgentMessageType.Error;
                return true;
            default:
                type = AgentMessageType.Content;
                return false;
        }
    }
}
