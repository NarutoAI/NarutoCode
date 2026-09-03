using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NarutoCode.Domain;
using NarutoCode.Infrastructure.JsonSerializerContexts;
using NarutoCode.Infrastructure.Tasks;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders.Tasks;

/// <summary>
/// Agent 任务状态的 JSON 文件存储。
/// 按业务会话 id 一会话一文件保存完整任务快照，用于进程重启或会话恢复后还原任务列表。
/// </summary>
internal sealed class AgentTaskStateStore
{
    /// <summary>
    /// 自定义存储目录；为 <see langword="null"/> 时使用默认数据目录。
    /// </summary>
    private readonly string? _customDirectory;

    /// <summary>
    /// 创建任务状态存储。
    /// </summary>
    /// <param name="directory">存储目录；为 <see langword="null"/> 时使用 <c>~/.narutocode/data/agent-tasks</c>。</param>
    public AgentTaskStateStore(string? directory = null)
    {
        this._customDirectory = directory;
    }

    /// <summary>
    /// 任务状态文件目录。延迟求值：应用数据根目录可能在启动阶段被宿主重新指定，不能在构造时固化。
    /// </summary>
    private string StoreDirectory =>
        this._customDirectory ?? Path.Combine(
            ProjectConstant.AppDirectory, ProjectConstant.DataDirectory, ProjectConstant.AgentTaskStatesDirectoryName);

    /// <summary>
    /// 延迟解析的日志器。该存储由贡献者直接构造、不走 DI，根容器未初始化时静默跳过日志。
    /// </summary>
    private static ILogger? Logger =>
        RootServiceProviderLocator.ServiceProvider?.GetService<ILogger<AgentTaskStateStore>>();

    /// <summary>
    /// 读取会话持久化的任务状态。
    /// </summary>
    /// <param name="sessionId">业务会话 id。</param>
    /// <returns>持久化的任务状态；文件不存在、损坏或会话 id 无效时返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// 保持同步：状态工厂是 <see cref="ProviderSessionState{TState}"/> 的同步委托，无法 await；
    /// 任务快照仅在会话首次访问时读取一次，同步阻塞可忽略。
    /// </remarks>
    public TaskAgentTaskState? Load(long sessionId)
    {
        // 会话 id 无效时不参与持久化（例如子 Agent 的临时会话）
        if (sessionId <= 0)
        {
            return null;
        }

        var filePath = this.GetFilePath(sessionId);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            // 读取文件内容后按源生成上下文反序列化
            var json = File.ReadAllBytes(filePath);
            var state = JsonSerializer.Deserialize(json, AIContentJsonSerializerContext.Default.TaskAgentTaskState);
            if (state is not null)
            {
                // 兼容外部编辑或旧版本生成的 items=null 文件，确保任务提供器始终获得可用列表
                state.Items ??= [];
            }

            return state;
        }
        catch (Exception exception)
        {
            // 文件损坏等异常不阻断会话：记录警告后回退空任务列表
            var logger = Logger;
            if (logger is not null)
            {
                Log.AgentTaskStateLoadFailed(logger, exception, sessionId);
            }

            return null;
        }
    }

    /// <summary>
    /// 异步写入会话完整任务状态。
    /// </summary>
    /// <param name="sessionId">业务会话 id。</param>
    /// <param name="state">当前任务状态快照。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 进程中断导致文件内容不完整时，<see cref="Load"/> 会捕获读取异常并回退空任务列表。
    /// </remarks>
    public async Task SaveAsync(long sessionId, TaskAgentTaskState state, CancellationToken cancellationToken = default)
    {
        // 会话 id 无效时不参与持久化
        if (sessionId <= 0)
        {
            return;
        }

        try
        {
            // 目录可能尚未创建，写入前确保存在
            Directory.CreateDirectory(this.StoreDirectory);

            var json = JsonSerializer.Serialize(state, AIContentJsonSerializerContext.Default.TaskAgentTaskState);
            await File.WriteAllTextAsync(this.GetFilePath(sessionId), json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 持久化失败不阻断任务更新：内存态仍已生效，仅记录警告
            var logger = Logger;
            if (logger is not null)
            {
                Log.AgentTaskStateSaveFailed(logger, exception, sessionId);
            }
        }
    }

    /// <summary>
    /// 获取指定会话的任务状态文件路径。
    /// </summary>
    private string GetFilePath(long sessionId) => Path.Combine(this.StoreDirectory, $"{sessionId}.json");
}
