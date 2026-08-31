using Microsoft.Extensions.Logging;
using NarutoCode.Infrastructure.AIAgents.Composition;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// Agent 运行时基础设施日志。
/// </summary>
internal static partial class Log
{
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
        Message = "已获取会话 Agent Runtime 租约：工作目录 {WorkingDirectory}，会话 {SessionId}。")]
    public static partial void ConversationAgentLeaseAcquired(ILogger logger, string workingDirectory, long sessionId);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Debug,
        Message = "已释放会话 Agent Runtime 租约：工作目录 {WorkingDirectory}，会话 {SessionId}。")]
    public static partial void ConversationAgentLeaseReleased(ILogger logger, string workingDirectory, long sessionId);

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Information,
        Message = "正在释放 AgentFactory：会话 Runtime 数 {RuntimeCount}")]
    public static partial void AgentFactoryDisposing(ILogger logger, int runtimeCount);

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

    [LoggerMessage(
        EventId = 15,
        Level = LogLevel.Debug,
        Message = "贡献者 {ContributorName} 不参与 {Profile} 档案装配，已跳过。")]
    public static partial void ContributorSkipped(ILogger logger, string contributorName, AgentProfile profile);

    [LoggerMessage(
        EventId = 16,
        Level = LogLevel.Warning,
        Message = "重置会话 Agent Runtime 时未找到缓存条目：工作目录 {WorkingDirectory}，会话 {SessionId}。")]
    public static partial void ConversationRuntimeNotFoundForReset(ILogger logger, string workingDirectory, long sessionId);

    [LoggerMessage(
        EventId = 17,
        Level = LogLevel.Information,
        Message = "会话 Agent Runtime 缓存条目被驱逐：工作目录 {WorkingDirectory}，会话 {SessionId}，原因 {Reason}。")]
    public static partial void ConversationRuntimeEvicted(ILogger logger, string workingDirectory, long sessionId, Microsoft.Extensions.Caching.Memory.EvictionReason reason);

    [LoggerMessage(
        EventId = 18,
        Level = LogLevel.Warning,
        Message = "会话 Agent Runtime 后台释放失败：工作目录 {WorkingDirectory}，会话 {SessionId}。")]
    public static partial void ConversationRuntimeDisposalFailed(ILogger logger, Exception exception, string workingDirectory, long sessionId);

    [LoggerMessage(
        EventId = 19,
        Level = LogLevel.Debug,
        Message = "ShellExecutor 已创建并纳入会话跟踪：工作目录 {WorkingDirectory}")]
    public static partial void ShellExecutorCreated(ILogger logger, string workingDirectory);

    [LoggerMessage(
        EventId = 20,
        Level = LogLevel.Warning,
        Message = "释放会话 ShellExecutor 失败")]
    public static partial void ShellExecutorDisposeFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 21,
        Level = LogLevel.Debug,
        Message = "会话 Shell 工厂释放完成：共回收 {Count} 个 Shell 子进程")]
    public static partial void ShellExecutorScopeDisposed(ILogger logger, int count);
}
