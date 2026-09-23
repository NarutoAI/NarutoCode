using System.Text.Json;
using System.Text.Json.Serialization;
using NarutoCode.Domain.Interactions;
using NarutoCode.Domain.Messages;

namespace NarutoCode.Infrastructure.JsonSerializerContexts;

/// <summary>
/// 会话 Item 消息类载荷：UI 渲染历史的持久化形态（agent_session_items.payload），
/// 字段与 TUI 渲染契约 ConversationHistoryMessage(Role, AgentMessage) 一一对应。
/// </summary>
/// <param name="Role">消息角色（ConversationMessageRole 名称文本）。</param>
/// <param name="Type">消息类型（AgentMessageTypeNames snake_case 文本）。</param>
/// <param name="Content">展示文本。</param>
/// <param name="ToolApprovalContent">工具审批请求的完整 JSON；非审批消息为空字符串。</param>
internal sealed record ConversationItemPayload(
    string Role,
    string Type,
    string Content,
    string ToolApprovalContent)
{
    /// <summary>
    /// 载荷创建时间；record 主构造无法与 init 属性共存序列化歧义，用辅助方法设置。
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
}

/// <summary>
/// 用户交互 item 的复合载荷（kind=userInteraction）：
/// 等待态只含请求；终态经 UserInteractionRepository.CompleteAsync 回填结果。
/// </summary>
/// <param name="Request">交互请求（弹窗渲染所需的完整意图数据）。</param>
/// <param name="Result">交互结果；等待中为 <see langword="null" />。</param>
internal sealed record UserInteractionItemPayload(
    UserInteractionRequest Request,
    UserInteractionResult? Result);

/// <summary>
/// 会话 Item 载荷 JSON 源生成上下文（NativeAOT 兼容）。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ConversationItemPayload))]
[JsonSerializable(typeof(UserInteractionItemPayload))]
[JsonSerializable(typeof(AgentMessageAttachment))]
[JsonSerializable(typeof(AgentMessageAttachment[]))]
internal sealed partial class ConversationItemJsonSerializerContext : JsonSerializerContext
{
    /// <summary>
    /// 序列化消息类 Item 载荷。
    /// </summary>
    /// <param name="payload">Item 载荷。</param>
    /// <returns>载荷 JSON 文本。</returns>
    internal static string SerializePayload(ConversationItemPayload payload)
    {
        return JsonSerializer.Serialize(payload, Default.ConversationItemPayload);
    }

    /// <summary>
    /// 反序列化消息类 Item 载荷；空载荷返回 <see langword="null" />。
    /// </summary>
    /// <param name="payload">持久化的载荷 JSON。</param>
    /// <returns>Item 载荷。</returns>
    internal static ConversationItemPayload? DeserializePayload(string payload)
    {
        return string.IsNullOrWhiteSpace(payload)
            ? null
            : JsonSerializer.Deserialize(payload, Default.ConversationItemPayload);
    }
}
