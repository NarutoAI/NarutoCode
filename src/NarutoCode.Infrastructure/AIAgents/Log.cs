using Microsoft.Extensions.Logging;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// Agent 运行时基础设施日志。
/// </summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Information,
        Message = "已创建工作目录 Agent 运行时池：{WorkingDirectory}。")]
    public static partial void WorkspaceAgentPoolCreated(ILogger logger, string workingDirectory);

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "正在回收空闲工作目录 Agent 运行时池：{WorkingDirectory}。")]
    public static partial void WorkspaceAgentPoolEvicting(ILogger logger, string workingDirectory);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "工作目录 Agent 运行时池已被回收，正在重新获取：{WorkingDirectory}。")]
    public static partial void WorkspaceAgentPoolReacquiring(ILogger logger, string workingDirectory);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "已创建会话 Agent Runtime：工作目录 {WorkingDirectory}。")]
    public static partial void ConversationAgentRuntimeCreated(ILogger logger, string workingDirectory);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "已标记会话 Agent Runtime 失效：工作目录 {WorkingDirectory}，会话 {SessionId}。")]
    public static partial void ConversationAgentRuntimeInvalidated(ILogger logger, string workingDirectory, long sessionId);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "正在释放失效的会话 Agent Runtime：工作目录 {WorkingDirectory}，会话 {SessionId}。")]
    public static partial void InvalidConversationAgentRuntimeDisposing(ILogger logger, string workingDirectory, long sessionId);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Debug,
        Message = "已获取会话 Agent Runtime 租约：工作目录 {WorkingDirectory}，会话 {SessionId}，活跃租约数 {ActiveLeaseCount}。")]
    public static partial void ConversationAgentLeaseAcquired(ILogger logger, string workingDirectory, long sessionId, int activeLeaseCount);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Debug,
        Message = "已释放会话 Agent Runtime 租约：工作目录 {WorkingDirectory}，会话 {SessionId}，活跃租约数 {ActiveLeaseCount}。")]
    public static partial void ConversationAgentLeaseReleased(ILogger logger, string workingDirectory, long sessionId, int activeLeaseCount);

    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Information,
        Message = "正在释放工作目录 Agent 运行时池：{WorkingDirectory}，会话 Runtime 数 {RuntimeCount}，活跃租约数 {ActiveLeaseCount}。")]
    public static partial void WorkspaceAgentPoolDisposing(ILogger logger, string workingDirectory, int runtimeCount, int activeLeaseCount);

    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Warning,
        Message = "重置会话 Agent Runtime 时未找到工作目录运行时池：工作目录 {WorkingDirectory}，会话 {SessionId}。")]
    public static partial void WorkspaceAgentPoolNotFoundForReset(ILogger logger, string workingDirectory, long sessionId);

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Information,
        Message = "正在释放 AgentFactory：工作目录池数量 {WorkspacePoolCount}")]
    public static partial void AgentFactoryDisposing(ILogger logger, int workspacePoolCount);

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Information,
        Message = "AgentFactory 已释放。")]
    public static partial void AgentFactoryDisposed(ILogger logger);

    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Error,
        Message = "创建会话 Agent Runtime 失败：工作目录 {WorkingDirectory}。")]
    public static partial void ConversationAgentRuntimeCreationFailed(ILogger logger, Exception exception, string workingDirectory);

    [LoggerMessage(
        EventId = 13,
        Level = LogLevel.Warning,
        Message = "配置的 Python 解释器路径不存在：{ConfiguredPath}，将自动探测可用解释器。")]
    public static partial void PythonExecutableConfiguredNotFound(ILogger logger, string configuredPath);

    [LoggerMessage(
        EventId = 14,
        Level = LogLevel.Warning,
        Message = "模型流式请求第 {Attempt} 次尝试传输断连（已产出 {Count} 个内容片段），{DelaySeconds}s 后自动恢复。")]
    public static partial void StreamingRequestRetrying(
        ILogger logger, Exception exception, int attempt, int count, double delaySeconds);
}
