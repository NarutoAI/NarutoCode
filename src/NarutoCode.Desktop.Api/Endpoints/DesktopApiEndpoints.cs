using NarutoCode.Desktop.Api.Contracts;

namespace NarutoCode.Desktop.Api.Endpoints;

/// <summary>
/// Desktop API 全部端点的集中注册入口。
/// </summary>
internal static class DesktopApiEndpoints
{
    /// <summary>
    /// 注册所有 Desktop API 端点。
    /// </summary>
    /// <param name="app">Web 应用实例。</param>
    public static WebApplication MapDesktopApiEndpoints(this WebApplication app)
    {
        app.MapHealthEndpoints();
        app.MapLlmSettingsEndpoints();
        app.MapWorkspaceEndpoints();
        app.MapConversationEndpoints();
        app.MapRunEndpoints();
        return app;
    }

    /// <summary>
    /// 健康检查端点。
    /// </summary>
    private static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => new HealthResponse(
            "ok",
            typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            Environment.ProcessId));
        return app;
    }
}
