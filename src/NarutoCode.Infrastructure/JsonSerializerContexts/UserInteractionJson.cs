using NarutoCode.Domain.Interactions;

namespace NarutoCode.Infrastructure.JsonSerializerContexts;

/// <summary>
/// 用户交互 JSON 的公开门面：CLI 等外部宿主构建/解析选择题结构化应答值。
/// 序列化上下文本体为 internal，经此门面暴露所需能力。
/// </summary>
public static class UserInteractionJson
{
    /// <summary>
    /// 将选中的选项 Id 集合序列化为交互结果值（JSON 数组文本）。
    /// </summary>
    /// <param name="selectedIds">选中的选项标识集合。</param>
    /// <returns>JSON 数组文本。</returns>
    public static string SerializeSelectedIds(IReadOnlyList<string> selectedIds)
    {
        return UserInteractionJsonSerializerContext.SerializeSelectedIds(selectedIds);
    }

    /// <summary>
    /// 序列化选择题的结构化应答值，包含选中选项与用户填写的补充说明。
    /// </summary>
    /// <param name="answer">选择题应答。</param>
    /// <returns>交互结果值（JSON 对象文本）。</returns>
    public static string SerializeSelectionAnswer(UserInteractionSelectionAnswer answer)
    {
        return UserInteractionJsonSerializerContext.SerializeSelectionAnswer(answer);
    }

    /// <summary>
    /// 从交互结果值解析选择题的结构化应答，兼容历史 JSON 数组。
    /// </summary>
    /// <param name="value">交互结果值 JSON 文本。</param>
    /// <returns>选择题结构化应答；空值或无效值返回空应答。</returns>
    public static UserInteractionSelectionAnswer DeserializeSelectionAnswer(string value)
    {
        return UserInteractionJsonSerializerContext.DeserializeSelectionAnswer(value);
    }

    /// <summary>
    /// 序列化批量问卷的结构化应答值。
    /// </summary>
    /// <param name="answer">批量问卷应答。</param>
    /// <returns>交互结果值（JSON 对象文本）。</returns>
    public static string SerializeBatchAnswer(UserInteractionBatchAnswer answer)
    {
        return UserInteractionJsonSerializerContext.SerializeBatchAnswer(answer);
    }

    /// <summary>
    /// 从交互结果值解析批量问卷的结构化应答。
    /// </summary>
    /// <param name="value">交互结果值 JSON 文本。</param>
    /// <returns>批量问卷应答；空值或无效值返回空应答。</returns>
    public static UserInteractionBatchAnswer DeserializeBatchAnswer(string value)
    {
        return UserInteractionJsonSerializerContext.DeserializeBatchAnswer(value);
    }

    /// <summary>
    /// 从交互结果值反序列化选中的选项 Id 集合。
    /// </summary>
    /// <param name="value">交互结果值 JSON 文本。</param>
    /// <returns>选中的选项标识集合；空值或无效值返回空集合。</returns>
    public static IReadOnlyList<string> DeserializeSelectedIds(string value)
    {
        return UserInteractionJsonSerializerContext.DeserializeSelectedIds(value);
    }
}
