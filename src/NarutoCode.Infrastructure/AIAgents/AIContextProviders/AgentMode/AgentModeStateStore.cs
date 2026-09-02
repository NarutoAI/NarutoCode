using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NarutoCode.Domain;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders.AgentMode;

/// <summary>
/// Agent 模式状态的 JSON 文件存储。
/// 按业务会话 id 一会话一文件保存当前运行模式，用于进程重启或会话恢复后还原模式。
/// </summary>
internal sealed class AgentModeStateStore
{
    /// <summary>
    /// 自定义存储目录；为 <see langword="null"/> 时使用默认数据目录。
    /// </summary>
    private readonly string? _customDirectory;

    /// <summary>
    /// 创建模式状态存储。
    /// </summary>
    /// <param name="directory">存储目录；为 <see langword="null"/> 时使用 <c>~/.narutocode/data/agent-modes</c>。</param>
    public AgentModeStateStore(string? directory = null)
    {
        this._customDirectory = directory;
    }

    /// <summary>
    /// 模式状态文件目录。延迟求值：应用数据根目录可能在启动阶段被宿主重新指定，不能在构造时固化。
    /// </summary>
    private string StoreDirectory =>
        this._customDirectory ?? Path.Combine(
            ProjectConstant.AppDirectory, ProjectConstant.DataDirectory, ProjectConstant.AgentModeStatesDirectoryName);

    /// <summary>
    /// 延迟解析的日志器。该存储由贡献者直接构造、不走 DI，根容器未初始化时静默跳过日志。
    /// </summary>
    private static ILogger? Logger =>
        RootServiceProviderLocator.ServiceProvider?.GetService<ILogger<AgentModeStateStore>>();

    /// <summary>
    /// 读取会话持久化的运行模式。
    /// </summary>
    /// <param name="sessionId">业务会话 id。</param>
    /// <returns>持久化的模式；文件不存在、损坏或会话 id 无效时返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// 保持同步：状态工厂是 <see cref="ProviderSessionState{TState}"/> 的同步委托，无法 await；
    /// 状态文件极小（约 50 字节），仅在会话首次访问时读取一次，同步阻塞可忽略。
    /// </remarks>
    public string? Load(long sessionId)
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
            var state = JsonSerializer.Deserialize(json, AIContentJsonSerializerContext.Default.ModeState);
            return string.IsNullOrWhiteSpace(state?.CurrentMode) ? null : state.CurrentMode;
        }
        catch (Exception exception)
        {
            // 文件损坏等异常不阻断会话：记录警告后回退默认模式
            var logger = Logger;
            if (logger is not null)
            {
                Log.AgentModeStateLoadFailed(logger, exception, sessionId);
            }

            return null;
        }
    }

    /// <summary>
    /// 异步写入会话运行模式。进程中断导致文件内容不完整时，<see cref="Load"/> 会捕获读取异常并返回空值。
    /// </summary>
    /// <param name="sessionId">业务会话 id。</param>
    /// <param name="mode">当前运行模式。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步写入操作的 <see cref="Task"/>。</returns>
    public async Task SaveAsync(long sessionId, string mode, CancellationToken cancellationToken = default)
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

            // 只持久化当前模式：构造全新状态落盘，通知标志等瞬态字段不带入文件
            var json = JsonSerializer.Serialize(
                new ModeState {CurrentMode = mode},
                AIContentJsonSerializerContext.Default.ModeState);

            var filePath = this.GetFilePath(sessionId);
            // 直接异步写入目标文件；如果进程中断导致 JSON 不完整，Load 会按损坏文件处理并返回空值
            await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 持久化失败不阻断模式切换：内存态仍已生效，仅记录警告
            var logger = Logger;
            if (logger is not null)
            {
                Log.AgentModeStateSaveFailed(logger, exception, sessionId);
            }
        }
    }

    /// <summary>
    /// 获取指定会话的模式状态文件路径。
    /// </summary>
    private string GetFilePath(long sessionId) => Path.Combine(this.StoreDirectory, $"{sessionId}.json");
}
