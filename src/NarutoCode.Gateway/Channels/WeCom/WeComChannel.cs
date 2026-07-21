using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NarutoCode.Gateway;
using NarutoCode.Gateway.Configuration;

namespace NarutoCode.Gateway.Channels.WeCom;

/// <summary>
/// 企业微信 AI 机器人通道。
/// 通过 WebSocket 长连接接收用户消息，优先用 WebSocket 回复、降级走 REST API。
/// 参考实现精简自 OpenClaw.Channels.WeComChannel。
/// </summary>
public sealed class WeComChannel : IGatewayChannel
{
    // ── 企业微信常量 ──
    private const string WsUrl = "wss://openws.work.weixin.qq.com";
    private const string ApiBase = "https://qyapi.weixin.qq.com";
    private const int HeartbeatMs = 30_000;

    private readonly WeComConfiguration _config;
    private readonly WeComMessageDedup _dedup = new();
    private readonly ILogger<WeComChannel> _logger;
    private readonly HttpClient _http = new();

    // WebSocket 发送锁，防止并发写入
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // 连接生命周期
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile ClientWebSocket? _ws;

    // 入站上下文缓存：用于 WebSocket 快速回复（企业微信要求回传 req_id，且 24h 内有效）
    private readonly ConcurrentDictionary<string, ReplyContext> _replyContexts = new(StringComparer.Ordinal);

    // access_token 缓存（REST API 降级用）
    private string? _accessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public string ChannelId => "wecom";

    public event Func<GatewayInboundMessage, CancellationToken, ValueTask>? OnMessageReceived;

    public WeComChannel(GatewayConfiguration gatewayConfig, ILogger<WeComChannel> logger)
    {
        _config = gatewayConfig.WeCom;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (!_config.Enabled)
        {
            Log.WeComChannelDisabled(_logger);
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(_config.BotId) || string.IsNullOrWhiteSpace(_config.BotSecret))
        {
            Log.WeComCredentialsMissing(_logger);
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = RunWsLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    // ════════════════════════════ WebSocket 长连接主循环 ════════════════════════════

    /// <summary>
    /// WebSocket 主循环：连接 → 认证 → 收消息 → 断线重连（指数退避，2s~60s）。
    /// </summary>
    private async Task RunWsLoopAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(2);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                await ws.ConnectAsync(new Uri(WsUrl), ct);

                _ws = ws; // 保存活动连接引用，供 SendAsync 回复使用

                await SendSubscribeAsync(ws, ct);
                backoff = TimeSpan.FromSeconds(2); // 连接成功后重置退避
                await ProcessMessagesAsync(ws, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.WeComWsConnectionError(_logger, backoff.TotalSeconds, ex);
            }
            finally
            {
                // 清理旧连接
                var old = Interlocked.Exchange(ref _ws, null);
                try { old?.Dispose(); } catch { }
            }

            if (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(backoff, ct); }
                catch (OperationCanceledException) { break; }
            }

            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 60));
        }
    }

    /// <summary>
    /// 发送认证订阅帧。
    /// </summary>
    private async Task SendSubscribeAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var reqId = Guid.NewGuid().ToString("N");
        var body = $"\"bot_id\":{JsonStr(_config.BotId!)},\"secret\":{JsonStr(_config.BotSecret!)}";
        var json = BuildFrame("aibot_subscribe", reqId, body);
        await SendRawAsync(ws, json, ct);
    }

    /// <summary>
    /// 消息处理循环：按需心跳 + 接收完整帧 → 分发处理。
    /// </summary>
    private async Task ProcessMessagesAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var lastPing = DateTimeOffset.UtcNow;

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            // 心跳保活
            if (DateTimeOffset.UtcNow - lastPing > TimeSpan.FromMilliseconds(HeartbeatMs))
            {
                await SendPingAsync(ws, ct);
                lastPing = DateTimeOffset.UtcNow;
            }

            // 读取完整帧到 StringBuilder，设置接收超时为 2 倍心跳间隔避免永久阻塞
            var sb = new StringBuilder();
            var messageComplete = false;
            var isClose = false;

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeoutCts.CancelAfter(HeartbeatMs * 2);

                try
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(buffer, timeoutCts.Token);
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    messageComplete = true;
                    isClose = result.MessageType == WebSocketMessageType.Close;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // 接收超时，补发心跳后继续下一轮
                    await SendPingAsync(ws, ct);
                    lastPing = DateTimeOffset.UtcNow;
                }
            }

            if (isClose)
                break;

            // 超时重发心跳或未收完完整帧时跳过本帧
            if (!messageComplete)
                continue;

            var json = sb.ToString();
            if (json.Length == 0)
                continue;

            try
            {
                await HandleFrameAsync(json, ct);
            }
            catch (Exception ex)
            {
                Log.WeComWsFrameError(_logger, json, ex);
            }
        }
    }

    /// <summary>
    /// 帧分发：按 cmd 字段路由。无 cmd 的为服务端响应帧（认证结果/心跳响应）。
    /// </summary>
    private async Task HandleFrameAsync(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var cmd = GetString(root, "cmd");
        var reqId = GetReqId(root);

        // 无 cmd → 服务端响应帧（认证/心跳结果）
        if (cmd is null)
        {
            var errCode = root.TryGetProperty("errcode", out var ec) ? ec.GetInt32() : 0;
            if (errCode != 0)
            {
                var errMsg = GetString(root, "errmsg");
                Log.WeComResponseError(_logger, reqId, errCode, errMsg);
            }
            return;
        }

        if (cmd == "aibot_msg_callback")
        {
            await HandleMsgCallbackAsync(root, reqId, ct);
        }
    }

    /// <summary>
    /// 处理消息回调：提取文本 → 去重 → 构造入站消息 → 触发事件。
    /// </summary>
    private async Task HandleMsgCallbackAsync(JsonElement root, string? reqId, CancellationToken ct)
    {
        if (!root.TryGetProperty("body", out var body))
            return;

        var msgId = GetString(body, "msgid");
        var chatId = GetString(body, "chatid");
        var chatType = GetString(body, "chattype");
        var senderId = body.TryGetProperty("from", out var from) ? GetString(from, "userid") : null;
        var senderName = body.TryGetProperty("from", out var from2) ? GetString(from2, "name") : null;
        var text = ReadText(body);

        // 消息去重
        if (!string.IsNullOrWhiteSpace(msgId) && !_dedup.TryClaim(msgId!))
            return;

        if (string.IsNullOrWhiteSpace(senderId) || string.IsNullOrWhiteSpace(text))
            return;

        // 截断超长消息
        if (text!.Length > _config.MaxInboundChars)
            text = text[.._config.MaxInboundChars];

        var isGroup = string.Equals(chatType, "group", StringComparison.OrdinalIgnoreCase);

        // 缓存回复上下文（24h 内可用 WebSocket 快速回复）
        if (!string.IsNullOrWhiteSpace(reqId))
        {
            if (!string.IsNullOrWhiteSpace(chatId))
                _replyContexts[chatId!] = new ReplyContext(reqId, DateTimeOffset.UtcNow);
            if (!string.IsNullOrWhiteSpace(senderId))
                _replyContexts[senderId] = new ReplyContext(reqId, DateTimeOffset.UtcNow);
        }

        var inbound = new GatewayInboundMessage(
            ChannelId: ChannelId,
            SenderId: senderId!,
            SenderName: senderName,
            Text: text!,
            MessageId: msgId,
            ReplyToId: reqId,
            GroupId: isGroup ? chatId : null,
            IsGroup: isGroup);

        if (OnMessageReceived is not null)
            await OnMessageReceived(inbound, ct);
    }

    /// <summary>
    /// 提取企业微信消息文本：支持纯文本和 mixed 混合消息中的文本部分。
    /// </summary>
    private static string? ReadText(JsonElement body)
    {
        // 纯文本消息
        if (body.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.Object)
        {
            var content = GetString(textProp, "content");
            if (!string.IsNullOrWhiteSpace(content))
                return content;
        }

        // mixed 混合消息（文本+图片），提取所有文本部分
        if (body.TryGetProperty("mixed", out var mixedProp) &&
            mixedProp.ValueKind == JsonValueKind.Object &&
            mixedProp.TryGetProperty("msg_item", out var items) &&
            items.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var item in items.EnumerateArray())
            {
                if (GetString(item, "msgtype") == "text" &&
                    item.TryGetProperty("text", out var itemText) &&
                    itemText.ValueKind == JsonValueKind.Object)
                {
                    var content = GetString(itemText, "content");
                    if (!string.IsNullOrWhiteSpace(content))
                        sb.Append(content);
                }
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        return null;
    }

    // ════════════════════════════ 发送回复 ════════════════════════════

    public async ValueTask SendAsync(string recipientId, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            // 优先 WebSocket 回复（24h 内有入站上下文）
            if (await TrySendViaWsAsync(recipientId, text, ct))
                return;

            // 降级 REST API
            if (HasApiCredentials())
            {
                await RefreshAccessTokenAsync(ct);
                await SendViaApiAsync(recipientId, text, ct);
            }
            else
            {
                Log.WeComApiCredentialsMissing(_logger, recipientId);
            }
        }
        catch (Exception ex)
        {
            Log.WeComSendFailed(_logger, recipientId, ex);
        }
    }

    /// <summary>
    /// 尝试通过 WebSocket 回复（需 24h 内入站上下文，回传原 req_id）。
    /// </summary>
    private async Task<bool> TrySendViaWsAsync(string recipientId, string text, CancellationToken ct)
    {
        if (!_replyContexts.TryGetValue(recipientId, out var ctx))
            return false;

        // 超过 24h 的上下文无效
        if (DateTimeOffset.UtcNow - ctx.ReceivedAt > TimeSpan.FromHours(24))
        {
            _replyContexts.TryRemove(recipientId, out _);
            return false;
        }

        var streamId = Guid.NewGuid().ToString("N");
        var body = "\"msgtype\":\"stream\"," +
                   $"\"stream\":{{\"id\":{JsonStr(streamId)},\"finish\":true,\"content\":{JsonStr(text)}}}";
        var json = BuildFrame("aibot_respond_msg", ctx.ReqId, body);

        return await SendWsTextAsync(json, ct);
    }

    /// <summary>
    /// 通过 REST API 发送文本消息（自建应用身份）。
    /// </summary>
    private async Task SendViaApiAsync(string recipientId, string text, CancellationToken ct)
    {
        // 企业微信文本消息限制 2048 字节
        if (Encoding.UTF8.GetByteCount(text) > 2048)
            text = TruncateToUtf8(text, 2048);

        var payload = new Dictionary<string, object>
        {
            ["touser"] = recipientId,
            ["msgtype"] = "text",
            ["agentid"] = _config.AgentId,
            ["text"] = new Dictionary<string, object> { ["content"] = text }
        };

        var json = JsonSerializer.Serialize(payload, WeComJsonContext.Default.DictionaryStringObject);
        var url = $"{ApiBase}/cgi-bin/message/send?access_token={_accessToken}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct);
            Log.WeComApiSendFailed(_logger, response.StatusCode.ToString(), errBody);
        }
    }

    /// <summary>检查是否具备 REST API 凭据。</summary>
    private bool HasApiCredentials()
        => !string.IsNullOrWhiteSpace(_config.CorpId) && !string.IsNullOrWhiteSpace(_config.CorpSecret);

    /// <summary>
    /// 获取/刷新 access_token，过期前 10 分钟自动刷新。
    /// </summary>
    private async Task RefreshAccessTokenAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-10))
            return;

        var url = $"{ApiBase}/cgi-bin/gettoken" +
                  $"?corpid={Uri.EscapeDataString(_config.CorpId!)}" +
                  $"&corpsecret={Uri.EscapeDataString(_config.CorpSecret!)}";

        using var response = await _http.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("errcode", out var ec) && ec.GetInt32() != 0)
        {
            var errMsg = GetString(root, "errmsg") ?? "unknown";
            throw new InvalidOperationException($"企业微信 access_token 获取失败：{ec.GetInt32()} {errMsg}");
        }

        _accessToken = root.TryGetProperty("access_token", out var token)
            ? token.GetString()
            : throw new InvalidOperationException("企业微信 access_token 响应缺失。");

        var expireSec = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 7200;
        _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expireSec);
    }

    // ════════════════════════════ WebSocket 辅助 ════════════════════════════

    /// <summary>发送心跳帧。</summary>
    private async Task SendPingAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var reqId = Guid.NewGuid().ToString("N");
        var json = BuildFrame("ping", reqId, null);
        await SendRawAsync(ws, json, ct);
    }

    /// <summary>构建企业微信 WebSocket 消息帧。</summary>
    private static string BuildFrame(string cmd, string reqId, string? bodyJson)
    {
        if (bodyJson is null)
            return $"{{\"cmd\":{JsonStr(cmd)},\"headers\":{{\"req_id\":{JsonStr(reqId)}}}}}";
        return $"{{\"cmd\":{JsonStr(cmd)},\"headers\":{{\"req_id\":{JsonStr(reqId)}}},\"body\":{{{bodyJson}}}}}";
    }

    /// <summary>直接向指定 WebSocket 发送文本帧。</summary>
    private async Task SendRawAsync(ClientWebSocket ws, string json, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            if (ws.State == WebSocketState.Open)
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>向活动 WebSocket 发送文本帧（线程安全）。成功返回 true。</summary>
    private async Task<bool> SendWsTextAsync(string json, CancellationToken ct)
    {
        var ws = _ws;
        if (ws is null || ws.State != WebSocketState.Open)
            return false;

        await _sendLock.WaitAsync(ct);
        try
        {
            if (ws.State == WebSocketState.Open)
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.WeComWsSendError(_logger, ex);
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // ════════════════════════════ 辅助方法 ════════════════════════════

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) ? p.GetString() : null;

    private static string? GetReqId(JsonElement root)
        => root.TryGetProperty("headers", out var h) ? GetString(h, "req_id") : null;

    /// <summary>JSON 字符串转义并加引号。</summary>
    private static string JsonStr(string value)
        => $"\"{JsonEncodedText.Encode(value)}\"";

    /// <summary>按 UTF-8 字节数截断字符串。</summary>
    private static string TruncateToUtf8(string s, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length <= maxBytes)
            return s;
        var truncated = new byte[maxBytes];
        Array.Copy(bytes, truncated, maxBytes);
        return Encoding.UTF8.GetString(truncated);
    }

    /// <summary>入站回复上下文缓存。</summary>
    private sealed record ReplyContext(string ReqId, DateTimeOffset ReceivedAt);

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            if (_loop is not null)
            {
                try { await _loop; }
                catch (OperationCanceledException) { }
            }
            _cts.Dispose();
        }
        _sendLock.Dispose();
        _http.Dispose();
    }
}

/// <summary>
/// 企业微信 API JSON 序列化上下文。
/// </summary>
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class WeComJsonContext : JsonSerializerContext;
