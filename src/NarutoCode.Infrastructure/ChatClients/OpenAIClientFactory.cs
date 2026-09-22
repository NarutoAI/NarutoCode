using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.Logging;
using NarutoCode.Domain.Configurations;
using OpenAI;
using Serilog;

namespace NarutoCode.Infrastructure.ChatClients;

internal static class OpenAIClientFactory
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(60);

    public static OpenAIClient Create(LlmConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.Address))
        {
            throw new InvalidOperationException("模型地址未填写。");
        }

        if (!Uri.TryCreate(configuration.Address, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("模型地址必须是有效的绝对地址。");
        }

        if (string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            throw new InvalidOperationException("模型 ApiKey 未填写。");
        }

        if (string.IsNullOrWhiteSpace(configuration.Model))
        {
            throw new InvalidOperationException("模型名称未填写。");
        }

        return new OpenAIClient(
            new ApiKeyCredential(configuration.ApiKey),
            new OpenAIClientOptions
            {
                Endpoint = endpoint,
                NetworkTimeout = NetworkTimeout,
                // 传输层统一改写 User-Agent 为产品标识（OpenAIClient.Dispose 不会释放共享 HttpClient）
                Transport = ProductHttpClient.SharedTransport,
                //启用日志
                // ClientLoggingOptions = new ClientLoggingOptions
                // {
                //     LoggerFactory = GetLoggerFactory(),
                //     //总开关：启用日志
                //     EnableLogging = true,
                //     EnableMessageLogging = true,
                //     // 记录请求/响应的行与头
                //     EnableMessageContentLogging = true,
                //     //记录请求/响应的完整内容
                //     MessageContentSizeLimit = 64 * 1024*1024
                // }
            });
    }
    // public static ILoggerFactory? GetLoggerFactory()
    // {
    //     // return default;
    //     Log.Logger = new LoggerConfiguration()
    //         .WriteTo.File(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Logs","log.txt"), rollingInterval: RollingInterval.Day)
    //         // .WriteTo.File()
    //         .MinimumLevel.Is(Serilog.Events.LogEventLevel.Verbose)
    //         .CreateLogger();
    //     return LoggerFactory.Create(a => { a.AddSerilog(); });
    // }
}
