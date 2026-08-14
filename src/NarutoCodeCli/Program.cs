using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NarutoCode.Domain.Messages;
using NarutoCode.Domain.Workspaces;
using NarutoCode.Infrastructure;
using NarutoCodeCli.Ui;
using NarutoCodeCli.Workspaces;

ConfigureConsoleEncoding();

using var cancellationTokenSource = new CancellationTokenSource();
var chatCancellationCoordinator = new ChatCancellationCoordinator();

// 所有异步初始化放到后台任务，主线程专职承担 Terminal.Gui 的 Init/Run/Shutdown 生命周期
var initializationTask = Task.Run(() =>
    InitializeServicesAsync(chatCancellationCoordinator, args, cancellationTokenSource.Token));
var initialization = initializationTask.GetAwaiter().GetResult();
var application = initialization.Application;
if (application is null)
{
    Console.WriteLine(initialization.Error);
    Console.ReadKey();
    return;
}

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    // 全屏 TUI 下 Ctrl+C 由聊天窗口按键处理；这里作为非交互/兜底路径的退出信号
    cancellationTokenSource.Cancel();
};

try
{
    application.Run(cancellationTokenSource.Token);
}
finally
{
    // 容器内含仅实现 IAsyncDisposable 的服务（如 AgentFactory），必须异步释放；
    // 走到此处时 Application 已确认非空，Provider 必然同生共死
    initialization.Provider!.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

return;

static async Task<InitializationResult> InitializeServicesAsync(
    ChatCancellationCoordinator chatCancellationCoordinator,
    string[] args,
    CancellationToken cancellationToken)
{
    var workspacePath = args.FirstOrDefault() ?? Environment.CurrentDirectory;
    var workspaceContext = new WorkspaceContext(workspacePath);
    var services = new ServiceCollection();
    services.AddSingleton<IWorkspaceContextAccessor>(new CliWorkspaceContextAccessor(workspaceContext));
    try
    {
        await services.AddInfrastructure();
    }
    catch (Exception e)
    {
        return new InitializationResult(e.Message, null, null);
    }

    services.AddSingleton(chatCancellationCoordinator);
    if (OperatingSystem.IsMacOS())
    {
        services.AddSingleton<IClipboardImageStore, MacOsClipboardImageStore>();
    }
    else
    {
        services.AddSingleton<IClipboardImageStore, NullClipboardImageStore>();
    }

    services.AddSingleton<PendingUserMessageQueue>();
    services.AddSingleton<TuiChatApplication>();

    var serviceProvider = services.BuildServiceProvider();
    await serviceProvider.BuildAsync();
    var application = serviceProvider.GetRequiredService<TuiChatApplication>();
    return new InitializationResult(null, application, serviceProvider);
}

static void ConfigureConsoleEncoding()
{
    var utf8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    if (!Console.IsOutputRedirected)
    {
        Console.OutputEncoding = utf8Encoding;
    }

    if (!Console.IsInputRedirected)
    {
        Console.InputEncoding = utf8Encoding;
    }
}

/// <summary>
/// 应用初始化结果；包含失败信息或可直接运行的 TUI 应用与容器。
/// </summary>
/// <param name="Error">初始化失败信息；成功时为 <see langword="null" />。</param>
/// <param name="Application">初始化成功的 TUI 应用。</param>
/// <param name="Provider">服务容器，应用运行期间必须保持存活。</param>
internal sealed record InitializationResult(string? Error, TuiChatApplication? Application, ServiceProvider? Provider);
