using NarutoCode.Domain.Configurations.Settings;
using NarutoCode.Domain.Enums;
using NarutoCode.Desktop.Api.Contracts;

namespace NarutoCode.Desktop.Api.Endpoints;

/// <summary>
/// LLM 设置相关端点。
/// </summary>
internal static class LlmSettingsEndpoints
{
    /// <summary>
    /// 注册 LLM 设置端点。
    /// </summary>
    public static WebApplication MapLlmSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/settings/llm");

        // 获取当前 LLM 设置
        group.MapGet("/", (ILlmSettingsService settings) =>
        {
            var response = new LlmSettingsResponse(
                settings.CurrentProvider,
                settings.CurrentEffort.ToString(),
                settings.GetAvailableProviders(),
                settings.GetAvailableEfforts().Select(e => e.ToString()).ToList());
            return Results.Ok(response);
        });

        // 切换 provider
        group.MapPut("/provider", (SwitchProviderRequest request, ILlmSettingsService settings) =>
        {
            if (string.IsNullOrWhiteSpace(request.Provider))
            {
                return Results.BadRequest(new { code = "invalid_provider", message = "provider 不能为空。" });
            }

            settings.SwitchProvider(request.Provider);
            return Results.Ok(new { provider = settings.CurrentProvider });
        });

        // 切换推理强度
        group.MapPut("/effort", (SwitchEffortRequest request, ILlmSettingsService settings) =>
        {
            if (!Enum.TryParse<LlmEffort>(request.Effort, ignoreCase: true, out var effort))
            {
                return Results.BadRequest(new { code = "invalid_effort", message = $"无效的推理强度：{request.Effort}" });
            }

            settings.SwitchEffort(effort);
            return Results.Ok(new { effort = settings.CurrentEffort.ToString() });
        });

        return app;
    }
}
