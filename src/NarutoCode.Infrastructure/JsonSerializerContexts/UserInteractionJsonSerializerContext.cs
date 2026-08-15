using System.Text.Json;
using System.Text.Json.Serialization;
using NarutoCode.Domain.Interactions;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;

namespace NarutoCode.Infrastructure.JsonSerializerContexts;

/// <summary>
/// 用户交互 JSON 源生成上下文：交互请求/结果的持久化序列化与 ask_user 工具参数反序列化。
/// 与 AIContentJsonSerializerContext 同为 internal：工具参数 DTO 是程序集内部类型，
/// 对外（CLI）经 UserInteractionJson 门面访问，满足 NativeAOT/裁剪约定。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(UserInteractionRequest))]
[JsonSerializable(typeof(UserInteractionResult))]
[JsonSerializable(typeof(UserInteractionOption))]
[JsonSerializable(typeof(List<UserInteractionOption>))]
[JsonSerializable(typeof(IReadOnlyList<UserInteractionOption>))]
[JsonSerializable(typeof(UserInteractionQuestion))]
[JsonSerializable(typeof(IReadOnlyList<UserInteractionQuestion>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(UserInteractionSelectionAnswer))]
[JsonSerializable(typeof(Dictionary<string, UserInteractionSelectionAnswer>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, UserInteractionSelectionAnswer>))]
[JsonSerializable(typeof(UserInteractionBatchAnswer))]
[JsonSerializable(typeof(AskUserQuestionRequest))]
[JsonSerializable(typeof(AskUserInputRequest))]
[JsonSerializable(typeof(AskUserOptionRequest))]
[JsonSerializable(typeof(AskUserBatchQuestionRequest))]
internal sealed partial class UserInteractionJsonSerializerContext : JsonSerializerContext
{
    /// <summary>
    /// 序列化交互请求为 Payload JSON。
    /// </summary>
    /// <param name="request">交互请求。</param>
    /// <returns>请求 JSON 文本。</returns>
    internal static string SerializeRequest(UserInteractionRequest request)
    {
        return JsonSerializer.Serialize(request, Default.UserInteractionRequest);
    }

    /// <summary>
    /// 从 Payload JSON 还原交互请求；空负载返回 <see langword="null" />。
    /// </summary>
    /// <param name="payload">持久化的请求 JSON。</param>
    /// <returns>交互请求。</returns>
    internal static UserInteractionRequest? DeserializeRequest(string payload)
    {
        return string.IsNullOrWhiteSpace(payload)
            ? null
            : JsonSerializer.Deserialize(payload, Default.UserInteractionRequest);
    }

    /// <summary>
    /// 序列化交互结果为 Result JSON。
    /// </summary>
    /// <param name="result">交互结果。</param>
    /// <returns>结果 JSON 文本。</returns>
    internal static string SerializeResult(UserInteractionResult result)
    {
        return JsonSerializer.Serialize(result, Default.UserInteractionResult);
    }

    /// <summary>
    /// 构建兼容历史数据的选择题应答值：仅保存选中选项 Id 的 JSON 数组文本。
    /// </summary>
    /// <param name="selectedIds">选中的选项标识集合。</param>
    /// <returns>JSON 数组文本，如 ["a","b"]。</returns>
    internal static string SerializeSelectedIds(IReadOnlyList<string> selectedIds)
    {
        return JsonSerializer.Serialize(selectedIds.ToArray(), Default.StringArray);
    }

    /// <summary>
    /// 序列化选择题的结构化应答值，包含选中选项与用户填写的补充说明。
    /// </summary>
    /// <param name="answer">选择题应答。</param>
    /// <returns>JSON 对象文本。</returns>
    internal static string SerializeSelectionAnswer(UserInteractionSelectionAnswer answer)
    {
        return JsonSerializer.Serialize(answer, Default.UserInteractionSelectionAnswer);
    }

    /// <summary>
    /// 解析选择题的结构化应答值，兼容旧版仅保存选中 Id 的 JSON 数组。
    /// </summary>
    /// <param name="value">应答值 JSON 文本。</param>
    /// <returns>选择题结构化应答；无效或空值返回空应答。</returns>
    internal static UserInteractionSelectionAnswer DeserializeSelectionAnswer(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new UserInteractionSelectionAnswer([], string.Empty);
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var selectedIds = JsonSerializer.Deserialize(value, Default.StringArray) ?? [];
                return new UserInteractionSelectionAnswer(selectedIds, string.Empty);
            }

            var answer = JsonSerializer.Deserialize(value, Default.UserInteractionSelectionAnswer);
            return new UserInteractionSelectionAnswer(answer?.SelectedIds ?? [], answer?.Supplement ?? string.Empty);
        }
        catch (JsonException)
        {
            return new UserInteractionSelectionAnswer([], string.Empty);
        }
    }

    /// <summary>
    /// 序列化批量问卷的结构化应答值。
    /// </summary>
    /// <param name="answer">批量问卷应答。</param>
    /// <returns>JSON 对象文本。</returns>
    internal static string SerializeBatchAnswer(UserInteractionBatchAnswer answer)
    {
        return JsonSerializer.Serialize(answer, Default.UserInteractionBatchAnswer);
    }

    /// <summary>
    /// 解析批量问卷的结构化应答值；空值或无效值返回空应答。
    /// </summary>
    /// <param name="value">应答值 JSON 文本。</param>
    /// <returns>批量问卷应答。</returns>
    internal static UserInteractionBatchAnswer DeserializeBatchAnswer(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new UserInteractionBatchAnswer(new Dictionary<string, UserInteractionSelectionAnswer>(), string.Empty);
        }

        try
        {
            var answer = JsonSerializer.Deserialize(value, Default.UserInteractionBatchAnswer);
            return new UserInteractionBatchAnswer(
                answer?.Answers ?? new Dictionary<string, UserInteractionSelectionAnswer>(),
                answer?.Supplement ?? string.Empty);
        }
        catch (JsonException)
        {
            return new UserInteractionBatchAnswer(new Dictionary<string, UserInteractionSelectionAnswer>(), string.Empty);
        }
    }

    /// <summary>
    /// 解析选择题应答值为选项 Id 集合。
    /// </summary>
    /// <param name="value">应答值 JSON 文本。</param>
    /// <returns>选项标识集合；空值或无效值返回空集合。</returns>
    internal static IReadOnlyList<string> DeserializeSelectedIds(string value)
    {
        return DeserializeSelectionAnswer(value).SelectedIds;
    }
}
