using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.Unicode;
using NarutoCode.Domain.Interactions;
using NarutoCode.Domain.Messages;

namespace NarutoCode.Infrastructure.JsonSerializerContexts;

/// <summary>
/// 用户输入消息 Item 载荷（kind=userMessage）：真实用户输入的完整展示数据。
/// </summary>
/// <param name="Text">用户输入文本。</param>
/// <param name="Attachments">图片附件集合（base64 + MIME），无附件为空数组。</param>
internal sealed record UserMessageItemPayload(
    string Text,
    IReadOnlyList<AgentMessageAttachment>? Attachments);

/// <summary>
/// 助手文本输出 Item 载荷（kind=agentMessage）。
/// </summary>
/// <param name="Text">完整输出文本（流式聚合后的最终快照）。</param>
internal sealed record AgentMessageItemPayload(string Text);

/// <summary>
/// 思考/推理 Item 载荷（kind=reasoning）。
/// </summary>
/// <param name="Text">完整思考文本（流式聚合后的最终快照）。</param>
internal sealed record ReasoningItemPayload(string Text);

/// <summary>
/// 工具调用 Item 载荷（kind=toolCall）：结构化记录调用参数与执行结果。
/// </summary>
/// <param name="ToolName">工具名称。</param>
/// <param name="Arguments">调用参数（格式化文本）。</param>
/// <param name="Result">执行结果摘要；执行中断 / provider 未回传结果时为 <see langword="null" />。</param>
internal sealed record ToolCallItemPayload(
    string ToolName,
    string Arguments,
    string? Result);

/// <summary>
/// 工具审批请求 Item 载荷（kind=toolApprovalRequest）：
/// 审批上下文 JSON 供 TUI 还原审批卡片并构建响应消息。
/// </summary>
/// <param name="ToolName">被审批的工具名称。</param>
/// <param name="ApprovalContent">ToolApprovalRequestContent 序列化 JSON（响应审批时回传）。</param>
internal sealed record ToolApprovalRequestItemPayload(
    string ToolName,
    string ApprovalContent);

/// <summary>
/// 错误 Item 载荷（kind=error）。
/// </summary>
/// <param name="Message">错误描述文本。</param>
internal sealed record ErrorItemPayload(string Message);

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
/// payload 结构随 agent_session_items.kind 不同而不同，
/// 读取端按 kind 选择对应 record 反序列化（kind 列即判别字段）。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(UserMessageItemPayload))]
[JsonSerializable(typeof(AgentMessageItemPayload))]
[JsonSerializable(typeof(ReasoningItemPayload))]
[JsonSerializable(typeof(ToolCallItemPayload))]
[JsonSerializable(typeof(ToolApprovalRequestItemPayload))]
[JsonSerializable(typeof(ErrorItemPayload))]
[JsonSerializable(typeof(UserInteractionItemPayload))]
[JsonSerializable(typeof(AgentMessageAttachment))]
[JsonSerializable(typeof(AgentMessageAttachment[]))]
internal sealed partial class ConversationItemJsonSerializerContext : JsonSerializerContext
{
    /// <summary>
    /// 支持全 Unicode 字符的派生上下文（懒加载）：
    /// 从源上下文 Options 派生并覆盖 Encoder，中文等非 ASCII 字符按原文落库，不转义为 \uXXXX。
    /// 必须延迟初始化：源生成器的 Default 单例与本类同为静态成员，
    /// 静态构造期间访问 Default.Options 会因循环初始化抛 NullReferenceException（测试已验证）。
    /// </summary>
    private static readonly Lazy<ConversationItemJsonSerializerContext> UnicodeContextLazy =
        new(() => new ConversationItemJsonSerializerContext(
            new JsonSerializerOptions(Default.Options)
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
            }));

    /// <summary>
    /// 序列化指定类型的 Item 载荷为 JSON 文本。
    /// </summary>
    /// <typeparam name="T">载荷 record 类型。</typeparam>
    /// <param name="payload">Item 载荷。</param>
    /// <returns>载荷 JSON 文本。</returns>
    internal static string SerializePayload<T>(T payload) => payload switch
    {
        UserMessageItemPayload p => JsonSerializer.Serialize(p, UnicodeContextLazy.Value.UserMessageItemPayload),
        AgentMessageItemPayload p => JsonSerializer.Serialize(p, UnicodeContextLazy.Value.AgentMessageItemPayload),
        ReasoningItemPayload p => JsonSerializer.Serialize(p, UnicodeContextLazy.Value.ReasoningItemPayload),
        ToolCallItemPayload p => JsonSerializer.Serialize(p, UnicodeContextLazy.Value.ToolCallItemPayload),
        ToolApprovalRequestItemPayload p => JsonSerializer.Serialize(p,
            UnicodeContextLazy.Value.ToolApprovalRequestItemPayload),
        ErrorItemPayload p => JsonSerializer.Serialize(p, UnicodeContextLazy.Value.ErrorItemPayload),
        UserInteractionItemPayload p =>
            JsonSerializer.Serialize(p, UnicodeContextLazy.Value.UserInteractionItemPayload),
        _ => throw new NotSupportedException($"不支持的 Item 载荷类型：{typeof(T).Name}")
    };

    /// <summary>
    /// 从载荷 JSON 反序列化为指定类型的 Item 载荷；空载荷返回 <see langword="null" />，
    /// 损坏载荷返回 <see langword="null" />（单条脏数据不阻断历史渲染）。
    /// </summary>
    /// <typeparam name="T">载荷 record 类型。</typeparam>
    /// <param name="payload">持久化的载荷 JSON。</param>
    /// <returns>Item 载荷。</returns>
    internal static T? DeserializePayload<T>(string payload) where T : class
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var info = typeof(T).Name switch
        {
            nameof(UserMessageItemPayload) => (JsonTypeInfo?) UnicodeContextLazy.Value.UserMessageItemPayload,
            nameof(AgentMessageItemPayload) => UnicodeContextLazy.Value.AgentMessageItemPayload,
            nameof(ReasoningItemPayload) => UnicodeContextLazy.Value.ReasoningItemPayload,
            nameof(ToolCallItemPayload) => UnicodeContextLazy.Value.ToolCallItemPayload,
            nameof(ToolApprovalRequestItemPayload) => UnicodeContextLazy.Value.ToolApprovalRequestItemPayload,
            nameof(ErrorItemPayload) => UnicodeContextLazy.Value.ErrorItemPayload,
            nameof(UserInteractionItemPayload) => UnicodeContextLazy.Value.UserInteractionItemPayload,
            _ => null
        };
        if (info is null)
        {
            throw new NotSupportedException($"不支持的 Item 载荷类型：{typeof(T).Name}");
        }

        try
        {
            return JsonSerializer.Deserialize(payload, info) as T;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}