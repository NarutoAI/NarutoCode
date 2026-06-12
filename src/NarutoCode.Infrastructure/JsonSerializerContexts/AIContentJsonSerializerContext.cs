using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;

namespace NarutoCode.Infrastructure.JsonSerializerContexts;

[JsonSerializable(typeof(DataContent))]
[JsonSerializable(typeof(ErrorContent))]
[JsonSerializable(typeof(FunctionCallContent))]
[JsonSerializable(typeof(FunctionResultContent))]
[JsonSerializable(typeof(HostedFileContent))]
[JsonSerializable(typeof(HostedVectorStoreContent))]
[JsonSerializable(typeof(TextContent))]
[JsonSerializable(typeof(TextReasoningContent))]
[JsonSerializable(typeof(UriContent))]
[JsonSerializable(typeof(UsageContent))]
[JsonSerializable(typeof(ToolCallContent))]
[JsonSerializable(typeof(ToolResultContent))]
[JsonSerializable(typeof(InputRequestContent))]
[JsonSerializable(typeof(InputResponseContent))]
[JsonSerializable(typeof(ToolApprovalRequestContent))]
[JsonSerializable(typeof(ToolApprovalResponseContent))]
[JsonSerializable(typeof(McpServerToolCallContent))]
[JsonSerializable(typeof(McpServerToolResultContent))]
[JsonSerializable(typeof(ImageGenerationToolCallContent))]
[JsonSerializable(typeof(ImageGenerationToolResultContent))]
[JsonSerializable(typeof(CodeInterpreterToolCallContent))]
[JsonSerializable(typeof(CodeInterpreterToolResultContent))]
[JsonSerializable(typeof(WebSearchToolCallContent))]
[JsonSerializable(typeof(WebSearchToolResultContent))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(AIContent))]
[JsonSerializable(typeof(AIContent[]))]
[JsonSerializable(typeof(List<AIContent>))]
[JsonSerializable(typeof(IList<AIContent>))]
[JsonSerializable(typeof(IDictionary<string, object?>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(IDictionary<string, System.Text.Json.JsonElement?>))]
[JsonSerializable(typeof(Dictionary<string, System.Text.Json.JsonElement?>))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
[JsonSerializable(typeof(Dictionary<string, System.Text.Json.JsonElement>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(ToolApprovalRequestContent))]
[JsonSerializable(typeof(TaskToolResult))]
[JsonSerializable(typeof(TaskCreateToolResult))]
[JsonSerializable(typeof(TaskGetToolResult))]
[JsonSerializable(typeof(TaskListToolResult))]
[JsonSerializable(typeof(TaskUpdateToolResult))]
[JsonSerializable(typeof(TaskStopToolResult))]
[JsonSerializable(typeof(TaskDetailedToolResult))]
[JsonSerializable(typeof(TaskListItemToolResult))]
[JsonSerializable(typeof(TaskStatusChangeToolResult))]
[JsonSerializable(typeof(TaskListItemToolResult[]))]
[JsonSerializable(typeof(SvgRenderToolResult))]
[JsonSerializable(typeof(SvgRenderMetadataResult))]
internal sealed partial class AIContentJsonSerializerContext : JsonSerializerContext
{
    /// <summary>
    /// 将持久化的模型内容反序列化为 AI 内容集合。
    /// </summary>
    /// <param name="modelContent">持久化的模型内容 JSON。</param>
    /// <returns>AI 内容集合。</returns>
    public static IList<AIContent> DeserializeContents(string modelContent)
    {
        if (string.IsNullOrWhiteSpace(modelContent))
        {
            return [];
        }

        return JsonSerializer.Deserialize(modelContent, Default.IListAIContent) ?? [];
    }

    /// <summary>
    /// 将 AI 内容集合序列化为可持久化的模型内容 JSON。
    /// </summary>
    /// <param name="contents">AI 内容集合。</param>
    /// <returns>模型内容 JSON。</returns>
    public static string SerializeContents(IList<AIContent> contents)
    {
        return JsonSerializer.Serialize(contents, Default.IListAIContent);
    }
    
    /// <summary>
    /// 
    /// </summary>
    public static ToolApprovalRequestContent? DeserializeToolApprovalRequestContent(string modelContent)
    {
        if (string.IsNullOrWhiteSpace(modelContent))
        {
            return null;
        }

        return JsonSerializer.Deserialize(modelContent, Default.ToolApprovalRequestContent) ?? default;
    }
    /// <summary>
    /// </summary>
    public static string SerializeToolApprovalRequestContent(ToolApprovalRequestContent content)
    {

        return JsonSerializer.Serialize(content, Default.ToolApprovalRequestContent);
    }
}