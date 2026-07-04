using Microsoft.Extensions.Logging;

namespace NarutoCode.Infrastructure.AIAgents.Mcp;

/// <summary>
/// MCP 基础设施日志。
/// </summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Warning,
        Message = "加载 MCP 服务 {ServerName} 工具失败。")]
    public static partial void McpServerToolLoadingFailed(ILogger logger, Exception exception, string serverName);

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "MCP 服务 {ServerName} 的工具名 {ToolName} 暴露后重复，已跳过。")]
    public static partial void DuplicateMcpToolNameSkipped(ILogger logger, string serverName, string toolName);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "MCP 服务 {ServerName} 的类型 {Type} 暂不支持。")]
    public static partial void UnsupportedMcpServerType(ILogger logger, string serverName, string type);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "MCP 服务 {ServerName} 缺少启动命令。")]
    public static partial void MissingMcpServerCommand(ILogger logger, string serverName);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "MCP 服务 {ServerName} 的工作目录不存在：{WorkingDirectory}")]
    public static partial void McpServerWorkingDirectoryNotFound(ILogger logger, string serverName, string workingDirectory);
}
