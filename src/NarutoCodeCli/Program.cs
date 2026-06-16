using System.Runtime.InteropServices;
using System.Text;
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
services.AddSingleton<SessionLauncherRenderer>();
services.AddSingleton<SessionLauncherPromptReader>();
services.AddSingleton<TuiChatApplication>();

await using var serviceProvider = services.BuildServiceProvider();
await serviceProvider.BuildAsync();
var application = serviceProvider.GetRequiredService<TuiChatApplication>();
await application.RunAsync(cancellationTokenSource.Token);

static void ConfigureAnsiConsole()
{
    ConfigureConsoleEncoding();

    if (Console.IsOutputRedirected)
    {
        return;
    }

    var isAnsiSupported = !OperatingSystem.IsWindows() || TryEnableWindowsVirtualTerminalProcessing();
    AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = isAnsiSupported ? AnsiSupport.Yes : AnsiSupport.No,
        ColorSystem = isAnsiSupported ? ColorSystemSupport.TrueColor : ColorSystemSupport.NoColors
    });
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

static bool TryEnableWindowsVirtualTerminalProcessing()
{
    const int standardOutputHandle = -11;
    const int enableVirtualTerminalProcessing = 0x0004;

    var consoleHandle = WindowsConsoleNative.GetStdHandle(standardOutputHandle);
    if (consoleHandle == IntPtr.Zero || consoleHandle == new IntPtr(-1))
    {
        return false;
    }

    if (!WindowsConsoleNative.GetConsoleMode(consoleHandle, out var consoleMode))
    {
        return false;
    }

    return WindowsConsoleNative.SetConsoleMode(consoleHandle, consoleMode | enableVirtualTerminalProcessing);
}

/// <summary>
/// Provides the minimal Windows console APIs required to enable ANSI escape sequence processing in legacy cmd hosts.
/// </summary>
internal static class WindowsConsoleNative
{
    /// <summary>
    /// Gets the native handle for a standard console stream.
    /// </summary>
    /// <param name="standardHandle">The standard stream identifier.</param>
    /// <returns>The native console handle.</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GetStdHandle(int standardHandle);

    /// <summary>
    /// Gets the current mode flags for a console handle.
    /// </summary>
    /// <param name="consoleHandle">The native console handle.</param>
    /// <param name="mode">The current console mode flags.</param>
    /// <returns><see langword="true" /> when the mode is read successfully.</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetConsoleMode(IntPtr consoleHandle, out int mode);

    /// <summary>
    /// Sets the mode flags for a console handle.
    /// </summary>
    /// <param name="consoleHandle">The native console handle.</param>
    /// <param name="mode">The requested console mode flags.</param>
    /// <returns><see langword="true" /> when the mode is applied successfully.</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetConsoleMode(IntPtr consoleHandle, int mode);
}
