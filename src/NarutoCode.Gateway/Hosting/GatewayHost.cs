using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NarutoCode.Domain;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Enums;
using NarutoCode.Domain.Workspaces;
using NarutoCode.Gateway.Bridge;
using NarutoCode.Gateway.Channels.WeCom;
using NarutoCode.Gateway.Configuration;
using NarutoCode.Infrastructure;

namespace NarutoCode.Gateway.Hosting;

/// <summary>

/// 网关宿主，按机器人绑定将消息送入对应根工作目录。

/// </summary>
public sealed class GatewayHost
{
    /// <summary>
    /// 启动网关并等待退出。
    /// </summary>
    public async Task RunAsync(GatewayOptions options, CancellationToken ct)
    {
        ProjectConstant.AppDirectory = options.AppDataDirectory;
        await AppData.InitAsync(ct);
        var config = await GatewayConfiguration.LoadAsync(options.ConfigPath);
        var services = new ServiceCollection();
        services.AddLogging();
        var workspaceAccessor = new GatewayWorkspaceContextAccessor();
        services.AddSingleton<IWorkspaceContextAccessor>(workspaceAccessor);
        await services.AddInfrastructure("gateway-.log");
        services.AddSingleton(config);
        services.AddSingleton<GatewayMessageBridge>();
        await using var provider = services.BuildServiceProvider();
        await provider.BuildAsync();

        var conversations = provider.GetRequiredService<IConversationService>();
        var bridge = provider.GetRequiredService<GatewayMessageBridge>();
        var logger = provider.GetRequiredService<ILogger<GatewayHost>>();
        var started = 0;
        foreach (var binding in config.WeComBots.Where(x => x.Enabled))
        {
            var workspace = await conversations.OpenWorkspaceBySourceAsync(binding.Workspace, ConversationSource.WeCom, ct);
            var channel = ActivatorUtilities.CreateInstance<WeComChannel>(provider, binding);
            channel.OnMessageReceived += (message, token) => HandleAsync(channel, binding, workspace.History.SessionId, message, token);
            await channel.StartAsync(ct);
            Log.ChannelStarted(logger, channel.ChannelId);
            started++;
        }
        if (started == 0) { Log.NoChannelEnabled(logger); return; }
        Console.WriteLine("网关已启动，按 Ctrl-C 退出。");
        await Task.Delay(Timeout.Infinite, ct);
        return;

        async ValueTask HandleAsync(WeComChannel channel, GatewayBotBinding binding, Domain.Messages.ConversationSessionId sessionId, Channels.GatewayInboundMessage message, CancellationToken token)
        {
            using var scope = workspaceAccessor.Push(binding.Workspace);
            await bridge.HandleAsync(channel, message, sessionId, token);
        }
    }
}
