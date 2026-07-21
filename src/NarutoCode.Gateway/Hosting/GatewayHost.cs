using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NarutoCode.Domain;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Workspaces;
using NarutoCode.Gateway;
using NarutoCode.Gateway.Bridge;
using NarutoCode.Gateway.Channels;
using NarutoCode.Gateway.Channels.WeCom;
using NarutoCode.Gateway.Configuration;
using NarutoCode.Infrastructure;

namespace NarutoCode.Gateway.Hosting;

/// <summary>
/// 网关宿主：负责 DI 容器构建、打开固定会话、按配置启动通道并绑定消息桥接。
/// </summary>
public sealed class GatewayHost
{
    /// <summary>
    /// 运行网关：初始化配置 → 注册DI → 打开会话 → 启动通道 → 阻塞等待退出。
    /// </summary>
    /// <param name="options">启动参数。</param>
    /// <param name="ct">退出令牌。</param>
    public async Task RunAsync(GatewayOptions options, CancellationToken ct)
    {
        // 1. 设置应用数据目录（复用 ~/.narutocode/）
        ProjectConstant.AppDirectory = options.AppDataDirectory;

        // 2. 初始化主配置（config.json，提供 LLM 配置等）
        await AppData.InitAsync(ct);

        // 3. 加载网关配置（gateway.json）
        var gatewayConfig = await GatewayConfiguration.LoadAsync(options.ConfigPath);

        // 4. 注册 DI（复用 Infrastructure 层的全部能力）
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IWorkspaceContextAccessor>(
            new GatewayWorkspaceContextAccessor(gatewayConfig.Workspace));
        await services.AddInfrastructure("gateway-.log");
        services.AddSingleton(gatewayConfig);
        services.AddSingleton<GatewayMessageBridge>();

        // 5. 按配置注册通道（目前只有企业微信，未来可扩展）
        if (gatewayConfig.WeCom.Enabled)
        {
            services.AddSingleton<IGatewayChannel, WeComChannel>();
        }

        await using var serviceProvider = services.BuildServiceProvider();
        await serviceProvider.BuildAsync();

        // 6. 打开固定工作目录会话（存在则加载最近一条，不存在则创建首个）
        var conversationService = serviceProvider.GetRequiredService<IConversationService>();
        var workspaceResult = await conversationService.OpenWorkspaceAsync(gatewayConfig.Workspace, ct);
        var sessionId = workspaceResult.History.SessionId;
        var logger = serviceProvider.GetRequiredService<ILogger<GatewayHost>>();

        Log.GatewayReady(logger, gatewayConfig.Workspace, sessionId.Value);

        // 7. 启动所有已注册通道 + 绑定消息桥接
        var bridge = serviceProvider.GetRequiredService<GatewayMessageBridge>();
        var channels = serviceProvider.GetServices<IGatewayChannel>().ToList();

        if (channels.Count == 0)
        {
            Log.NoChannelEnabled(logger);
            return;
        }

        foreach (var channel in channels)
        {
            var ch = channel; // 闭包捕获
            channel.OnMessageReceived += (msg, token) =>
                new ValueTask(bridge.HandleAsync(ch, msg, sessionId, token));
            await channel.StartAsync(ct);
            Log.ChannelStarted(logger, channel.ChannelId);
        }

        Console.WriteLine("网关已启动，按 Ctrl-C 退出。");

        // 8. 阻塞等待退出信号
        await Task.Delay(Timeout.Infinite, ct);
    }
}
