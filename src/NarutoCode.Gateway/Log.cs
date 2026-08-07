using Microsoft.Extensions.Logging;

namespace NarutoCode.Gateway;

/// <summary>
/// 网关源生成日志声明，禁止直接调用 logger.LogInformation/LogError。
/// </summary>
internal static partial class Log
{
    // ── 通道生命周期 ──

    [LoggerMessage(1000, LogLevel.Information, "企业微信通道未启用，跳过启动。")]
    public static partial void WeComChannelDisabled(ILogger logger);

    [LoggerMessage(1001, LogLevel.Error, "企业微信 BotId 或 BotSecret 未配置，通道无法启动。")]
    public static partial void WeComCredentialsMissing(ILogger logger);

    [LoggerMessage(1002, LogLevel.Information, "通道已启动：{ChannelId}")]
    public static partial void ChannelStarted(ILogger logger, string channelId);

    [LoggerMessage(1003, LogLevel.Information, "网关已就绪，工作目录：{Workspace}，会话：{SessionId}")]
    public static partial void GatewayReady(ILogger logger, string workspace, long sessionId);

    [LoggerMessage(1004, LogLevel.Warning, "未启用任何通道，请在 gateway.json 中配置并启用通道。")]
    public static partial void NoChannelEnabled(ILogger logger);

    // ── WebSocket 连接 ──

    [LoggerMessage(1010, LogLevel.Warning, "企业微信 WebSocket 连接异常，{Seconds} 秒后重连。")]
    public static partial void WeComWsConnectionError(ILogger logger, double seconds, Exception exception);

    [LoggerMessage(1011, LogLevel.Warning, "企业微信 WebSocket 帧处理异常：{Json}")]
    public static partial void WeComWsFrameError(ILogger logger, string json, Exception exception);

    [LoggerMessage(1012, LogLevel.Warning, "企业微信 WebSocket 发送失败。")]
    public static partial void WeComWsSendError(ILogger logger, Exception exception);

    [LoggerMessage(1013, LogLevel.Debug, "企业微信收到 WebSocket 帧 cmd={Cmd} req_id={ReqId} 长度={Length}")]
    public static partial void WeComFrameReceived(ILogger logger, string? cmd, string? reqId, int length);

    // ── 企业微信协议 ──

    [LoggerMessage(1020, LogLevel.Error, "企业微信响应失败 req_id={ReqId} errcode={ErrCode} errmsg={ErrMsg}")]
    public static partial void WeComResponseError(ILogger logger, string? reqId, int errCode, string? errMsg);

    [LoggerMessage(1021, LogLevel.Warning, "企业微信 REST API 凭据未配置，无法发送消息给 {RecipientId}。")]
    public static partial void WeComApiCredentialsMissing(ILogger logger, string recipientId);

    [LoggerMessage(1022, LogLevel.Warning, "企业微信 API 发送失败：{StatusCode} {Body}")]
    public static partial void WeComApiSendFailed(ILogger logger, string statusCode, string body);

    [LoggerMessage(1023, LogLevel.Error, "企业微信发送消息失败 recipientId={RecipientId}。")]
    public static partial void WeComSendFailed(ILogger logger, string recipientId, Exception exception);

    [LoggerMessage(1024, LogLevel.Warning, "企业微信图片下载或解密失败 url={Url}。")]
    public static partial void WeComImageDownloadFailed(ILogger logger, string url, Exception exception);

    // ── 消息桥接 ──

    [LoggerMessage(1030, LogLevel.Error, "桥接消息处理失败 channel={ChannelId} sender={SenderId}。")]
    public static partial void BridgeHandleFailed(ILogger logger, string channelId, string senderId, Exception exception);
}
