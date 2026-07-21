using NarutoCode.Gateway;
using NarutoCode.Gateway.Hosting;

// 干净的启动入口：仅做参数解析和退出信号处理，编排逻辑全部委托给 GatewayHost。
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var options = GatewayOptions.Parse(args);
var host = new GatewayHost();

try
{
    await host.RunAsync(options, cts.Token);
}
catch (OperationCanceledException)
{
    // 用户按 Ctrl-C 正常退出
}
catch (Exception ex)
{
    Console.Error.WriteLine($"网关启动失败：{ex.Message}");
    Environment.ExitCode = 1;
}
