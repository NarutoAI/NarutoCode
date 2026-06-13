namespace NarutoCode.Domain.Messages;

/// <summary>
/// Agent 返回消息的内容类型。
/// </summary>
public enum AgentMessageType
{
    /// <summary>
    /// 普通消息内容，用于展示给用户的最终文本。
    /// </summary>
    Content,

    /// <summary>
    /// 思考内容，用于表示模型推理或中间分析过程。
    /// </summary>
    Thinking,

    /// <summary>
    /// 工具调用内容，用于表示 Agent 请求执行外部工具。
    /// </summary>
    ToolCall,

    /// <summary>
    /// 标识当前是计划传递的消息
    /// </summary>
    Plan,

    /// <summary>
    /// 遗留任务
    /// </summary>
    RemainingTask,

    /// <summary>
    /// 工具审批
    /// </summary>
    ToolApprovalRequest,
    /// <summary>
    /// 审批结果
    /// </summary>
    ToolApprovalResponse,
    /// <summary>
    /// token用量
    /// </summary>
    Usage,
    /// <summary>
    /// 错误
    /// </summary>
    Error,
    
}