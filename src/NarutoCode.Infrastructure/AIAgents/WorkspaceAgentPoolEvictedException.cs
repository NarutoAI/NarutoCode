namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// 指示目录池已被回收，调用方应重新从工厂获取池。
/// </summary>
internal sealed class WorkspaceAgentPoolEvictedException(string workingDirectory)
    : InvalidOperationException($"工作目录运行时已回收：{workingDirectory}");
