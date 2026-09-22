using System.ClientModel.Primitives;

namespace NarutoCode.Infrastructure.ChatClients;

/// <summary>
/// 产品级 HTTP 传输层 User-Agent 统一改写。
/// 各 LLM SDK（OpenAI/MEAI/Anthropic/MCP）默认在请求头携带自身标识（如 OpenAI/2.11.0、MEAI/10.7.0），
/// 这里在传输层最后一步整体替换为产品标识 narutocode/{version}，SDK 内部策略无法再覆盖。
/// </summary>
internal static class ProductHttpClient
{
    /// <summary>
    /// 产品 User-Agent 值：narutocode/{程序集版本}。
    /// </summary>
    public static string UserAgent { get; } =
        $"narutocode/{typeof(ProductHttpClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

    /// <summary>
    /// OpenAI 兼容链路共享的传输器：挂在 OpenAIClientOptions.Transport，
    /// 实测 OpenAIClient.Dispose 不会释放外部传入的 HttpClient，可全局复用。
    /// </summary>
    public static PipelineTransport SharedTransport { get; } = new HttpClientPipelineTransport(CreateHttpClient());

    /// <summary>
    /// 创建挂载 UA 改写处理器、超时无限的 HttpClient。
    /// 超时必须无限：HttpClient 默认 100 秒会截断长流式响应，超时控制交由各 SDK 的
    /// NetworkTimeout/Timeout 机制（它们通过 CancellationToken 生效，不受 HttpClient.Timeout 影响）。
    /// </summary>
    public static HttpClient CreateHttpClient() => new(new UserAgentRewriteHandler(UserAgent))
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    /// <summary>
    /// Anthropic 链路专用 UA 改写处理器：通过 AnthropicClient.Handlers 注入，
    /// InnerHandler 由 SDK 接线，不可预置（预置会被 SDK 覆盖）。
    /// </summary>
    public sealed class AnthropicUserAgentHandler : DelegatingHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // 移除 SDK 写入的默认 UA 后写入产品标识
            request.Headers.Remove("User-Agent");
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// 传输层 UA 改写处理器：移除 SDK 写入的全部 User-Agent 后写入产品标识。
    /// 必须在构造时指定内层真实传输处理器，否则 HttpClient 发送时抛
    /// InvalidOperationException("The inner handler has not been assigned.")。
    /// </summary>
    private sealed class UserAgentRewriteHandler(string userAgent)
        : DelegatingHandler(new SocketsHttpHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // 先移除 SDK 管道（UserAgentPolicy/MeaiUserAgentPolicy 等）已写入的 UA，再写入产品标识
            request.Headers.Remove("User-Agent");
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
