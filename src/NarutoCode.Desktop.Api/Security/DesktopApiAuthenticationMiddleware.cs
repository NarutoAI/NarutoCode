using NarutoCode.Desktop.Api.Configuration;

namespace NarutoCode.Desktop.Api.Security;

/// <summary>
/// Bearer 令牌认证中间件，校验请求头中的 Authorization 值。
/// </summary>
internal sealed class DesktopApiAuthenticationMiddleware(
    RequestDelegate next,
    DesktopApiOptions options)
{
    /// <summary>
    /// 执行中间件逻辑，令牌不匹配时返回 401。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    public async Task InvokeAsync(HttpContext context)
    {
        // 构造期望的 Bearer 头并做精确比较
        var expected = $"Bearer {options.Token}";
        if (!string.Equals(context.Request.Headers.Authorization, expected, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}
