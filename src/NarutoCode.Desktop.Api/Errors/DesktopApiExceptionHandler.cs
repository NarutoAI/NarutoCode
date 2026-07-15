using Microsoft.AspNetCore.Diagnostics;
using NarutoCode.Desktop.Api.Runs;

namespace NarutoCode.Desktop.Api.Errors;

/// <summary>
/// 全局异常处理器，将类型化异常映射为稳定的错误响应。
/// </summary>
internal sealed class DesktopApiExceptionHandler(
    ILogger<DesktopApiExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    /// <summary>
    /// 捕获未处理异常并生成统一错误响应。
    /// </summary>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, code, message) = MapException(exception);
        var traceId = httpContext.TraceIdentifier;

        if (statusCode >= 500)
        {
            Log.DesktopApiServerError(logger, code, exception);
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new ApiErrorResponse(code, message, traceId, null),
            cancellationToken);

        return true;
    }

    /// <summary>
    /// 将异常类型映射为 HTTP 状态码和错误代码。
    /// </summary>
    private (int StatusCode, string Code, string Message) MapException(Exception exception)
    {
        return exception switch
        {
            RunAlreadyActiveException ex => (409, "run_already_active", ex.Message),
            RunNotFoundException ex => (404, "run_not_found", ex.Message),
            ConversationNotFoundException ex => (404, "conversation_not_found", ex.Message),
            ArgumentException => (400, "invalid_request", exception.Message),
            UnauthorizedAccessException => (401, "unauthorized", exception.Message),
            InvalidOperationException => (409, "conflict", exception.Message),
            _ => (500, "internal_error", "服务器内部错误。")
        };
    }
}
