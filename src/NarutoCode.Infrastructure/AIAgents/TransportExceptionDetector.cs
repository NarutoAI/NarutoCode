using System.IO;
using System.Net.Http;
using System.Net.Sockets;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// 传输层异常判定工具，用于识别模型流式请求中可安全重试的断连异常。
/// </summary>
internal static class TransportExceptionDetector
{
    /// <summary>
    /// 判断异常是否为传输层断连（连接被重置 / 流意外结束 / Socket 异常）。
    /// </summary>
    /// <param name="exception">需要判定的异常。</param>
    /// <returns>传输层断连时返回 <see langword="true" />；用户取消等受控中断不属于传输断连。</returns>
    public static bool IsTransportDisconnect(Exception exception)
    {
        // 递归检查异常链：OpenAI SDK 通常会把 SocketException 包装在 HttpRequestException / ClientResultException 内
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException or HttpIOException or IOException or SocketException)
            {
                return true;
            }
        }

        return false;
    }
}
