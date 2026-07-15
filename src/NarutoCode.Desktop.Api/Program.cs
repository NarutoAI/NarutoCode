using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Json;
using NarutoCode.Desktop.Api;
using NarutoCode.Desktop.Api.Configuration;
using NarutoCode.Desktop.Api.Contracts;
using NarutoCode.Desktop.Api.Endpoints;
using NarutoCode.Desktop.Api.Errors;
using NarutoCode.Desktop.Api.Hosting;
using NarutoCode.Desktop.Api.Runs;
using NarutoCode.Desktop.Api.Serialization;
using NarutoCode.Desktop.Api.Security;
using NarutoCode.Desktop.Api.Workspaces;
using NarutoCode.Domain;
using NarutoCode.Domain.Workspaces;
using NarutoCode.Infrastructure;

// 解析启动参数并设置应用数据目录，必须在 AddInfrastructure 之前完成
var options = DesktopApiOptions.Parse(Environment.GetEnvironmentVariables());
ProjectConstant.AppDirectory = options.AppDataDirectory;

// 初始化应用配置（config.json）
await AppData.InitAsync();

var builder = WebApplication.CreateSlimBuilder(args);

// 仅绑定回环地址，端口由 Electron Main 分配
builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, options.Port));
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<DesktopWorkspaceContextAccessor>();
builder.Services.AddSingleton<IWorkspaceContextAccessor>(serviceProvider =>
    serviceProvider.GetRequiredService<DesktopWorkspaceContextAccessor>());
builder.Services.AddHostedService<ParentProcessMonitor>();

// 复用 Infrastructure 层注册，使用桌面端专属日志文件名
await builder.Services.AddInfrastructure("desktop-api-.log");

// 注册 Run 协调器和异常处理器
builder.Services.AddSingleton<IDesktopRunCoordinator, DesktopRunCoordinator>();
builder.Services.AddExceptionHandler<DesktopApiExceptionHandler>();
builder.Services.AddProblemDetails();

// 注册 AOT 兼容的 JSON 序列化上下文
builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.TypeInfoResolverChain.Insert(0, DesktopApiJsonSerializerContext.Default));

var app = builder.Build();
await app.Services.BuildAsync();

// 注册中间件
app.UseMiddleware<DesktopApiAuthenticationMiddleware>();
app.UseExceptionHandler();

// 注册所有 API 端点
app.MapDesktopApiEndpoints();

// 启动成功后向 stdout 输出 ready 信号和端口
app.Lifetime.ApplicationStarted.Register(() =>
{
    if (app.Environment.IsEnvironment("Testing"))
    {
        return;
    }

    var server = app.Services.GetRequiredService<IServer>();
    var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses
                    ?? throw new InvalidOperationException("Desktop API 未公开监听地址。");
    var port = new Uri(addresses.Single()).Port;
    var ready = new ReadyResponse("ready", port);
    Console.Out.WriteLine(JsonSerializer.Serialize(ready, DesktopApiJsonSerializerContext.Default.ReadyResponse));
});

try
{
    await app.RunAsync();
}
catch (Exception exception)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    Log.DesktopApiStartupFailed(logger, exception);
    throw;
}

// 暴露 partial Program 类，供 WebApplicationFactory<Program> 集成测试使用
public partial class Program;
