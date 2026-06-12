using Microsoft.Extensions.DependencyInjection;
using NarutoCode.Domain.Messages;
using NarutoCode.Domain.Workspaces;
using NarutoCode.Infrastructure;
using NarutoCodeCli.Ui;
using NarutoCodeCli.Workspaces;
using Spectre.Console;

ConfigureAnsiConsole();

using var cancellationTokenSource = new CancellationTokenSource();
var chatCancellationCoordinator = new ChatCancellationCoordinator();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    if (!chatCancellationCoordinator.TryCancelCurrentOperation())
    {
        cancellationTokenSource.Cancel();
    }
};

var workspacePath = args.FirstOrDefault() ?? Environment.CurrentDirectory;
var workspaceContext = new WorkspaceContext(workspacePath);
var services = new ServiceCollection();
services.AddSingleton<IWorkspaceContextAccessor>(new CliWorkspaceContextAccessor(workspaceContext));
await services.AddInfrastructure();
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
services.AddSingleton<QueuedChatInputReader>();
services.AddSingleton<ChatPromptReader>();
services.AddSingleton<ChatScreenRenderer>();
services.AddSingleton<TuiChatApplication>();

await using var serviceProvider = services.BuildServiceProvider();
await serviceProvider.BuildAsync();
var application = serviceProvider.GetRequiredService<TuiChatApplication>();
await application.RunAsync(cancellationTokenSource.Token);

static void ConfigureAnsiConsole()
{
    if (Console.IsOutputRedirected)
    {
        return;
    }

    AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.Yes,
        ColorSystem = ColorSystemSupport.TrueColor
    });
}
