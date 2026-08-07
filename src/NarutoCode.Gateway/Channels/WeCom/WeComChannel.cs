using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
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

    private readonly GatewayBotBinding _config;
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

    public string ChannelId => "wecom:" + _config.Id;

    public event Func<GatewayInboundMessage, CancellationToken, ValueTask>? OnMessageReceived;

    public WeComChannel(GatewayBotBinding binding, ILogger<WeComChannel> logger)
    {
        _config = binding;
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

        // 记录收到的帧（含消息回调、认证/心跳响应等），便于调试追踪
        Log.WeComFrameReceived(_logger, cmd, reqId, json.Length);

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
    /// 处理消息回调：提取文本和图片（含引用消息）→ 去重 → 构造入站消息 → 触发事件。
    /// 支持纯文本、纯图片、mixed 混合消息，以及 quote 引用消息中的文本和图片。
    /// </summary>
    private async Task HandleMsgCallbackAsync(JsonElement root, string? reqId, CancellationToken ct)
    {
        if (!root.TryGetProperty("body", out var body))
            return;

        var aibotid = GetString(body, "aibotid");
        var msgId = GetString(body, "msgid");
        var chatId = GetString(body, "chatid");
        var chatType = GetString(body, "chattype");
        var senderId = body.TryGetProperty("from", out var from) ? GetString(from, "userid") : null;
        var senderName = body.TryGetProperty("from", out var from2) ? GetString(from2, "name") : null;
        var text = ReadText(body);
        //定义来源的id，用于绑定我们的会话id
        var sourceId=string.IsNullOrWhiteSpace(chatId)?$"{aibotid}_{senderId}":chatId ;
        // 提取并下载当前消息的图片附件
        var attachments = new List<GatewayInboundAttachment>();
        attachments.AddRange(await DownloadImagesAsync(body, ct));

        // 提取引用消息的文本和图片，引用内容的结构和 body 相同（msgtype/text/image/mixed）
        if (body.TryGetProperty("quote", out var quote) && quote.ValueKind == JsonValueKind.Object)
        {
            var quoteText = ReadText(quote);
            var quoteImages = await DownloadImagesAsync(quote, ct);

            if (!string.IsNullOrWhiteSpace(quoteText))
            {
                // 将引用文本以引用块形式拼接到用户消息前面
                text = string.IsNullOrWhiteSpace(text)
                    ? $"「引用消息」\n{quoteText}"
                    : $"「引用消息」\n{quoteText}\n\n{text}";
            }

            // 引用消息中的图片也作为附件传给 AI
            attachments.AddRange(quoteImages);
        }

        // 消息去重
        if (!string.IsNullOrWhiteSpace(msgId) && !_dedup.TryClaim(msgId!))
            return;

        // 文本和图片都为空时跳过
        if (string.IsNullOrWhiteSpace(senderId))
            return;
        if (string.IsNullOrWhiteSpace(text) && attachments.Count == 0)
            return;

        // 截断超长文本
        if (!string.IsNullOrWhiteSpace(text) && text!.Length > _config.MaxInboundChars)
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
            Text: text ?? string.Empty,
            MessageId: msgId,
            ReplyToId: reqId,
            GroupId: isGroup ? chatId : null,
            IsGroup: isGroup,
            SourceId: sourceId,
            Attachments: attachments);

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

    // ════════════════════════════ 图片下载与解密 ════════════════════════════

    /// <summary>
    /// 从消息体中提取所有图片 URL 与 aeskey，并下载解密到本地临时目录。
    /// 企业微信 AI Bot 的 mixed 图片项提供的是 5 分钟有效的加密文件 URL，不能按 media_id 下载。
    /// </summary>
    private async Task<IReadOnlyList<GatewayInboundAttachment>> DownloadImagesAsync(
        JsonElement body, CancellationToken ct)
    {
        var images = new List<WeComImagePayload>();

        // 纯图片消息：body.image.url / body.image.aeskey
        if (body.TryGetProperty("image", out var img) && img.ValueKind == JsonValueKind.Object)
        {
            TryAddImagePayload(images, img);
        }

        // mixed 混合消息：body.mixed.msg_item[].image.url / aeskey
        if (body.TryGetProperty("mixed", out var mixedProp) &&
            mixedProp.ValueKind == JsonValueKind.Object &&
            mixedProp.TryGetProperty("msg_item", out var items) &&
            items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (GetString(item, "msgtype") == "image" &&
                    item.TryGetProperty("image", out var itemImage) &&
                    itemImage.ValueKind == JsonValueKind.Object)
                {
                    TryAddImagePayload(images, itemImage);
                }
            }
        }

        if (images.Count == 0)
            return [];

        var attachments = new List<GatewayInboundAttachment>(images.Count);
        foreach (var image in images)
        {
            try
            {
                var attachment = await DownloadAndDecryptImageAsync(image, ct);
                if (attachment is not null)
                    attachments.Add(attachment);
            }
            catch (Exception ex)
            {
                Log.WeComImageDownloadFailed(_logger, image.Url, ex);
            }
        }

        return attachments;
    }

    /// <summary>
    /// 尝试从企业微信图片节点读取 url 与 aeskey。
    /// </summary>
    private static void TryAddImagePayload(List<WeComImagePayload> images, JsonElement image)
    {
        var url = GetString(image, "url");
        var aesKey = GetString(image, "aeskey");
        if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(aesKey))
            images.Add(new WeComImagePayload(url!, aesKey!));
    }

    /// <summary>
    /// 下载企业微信临时图片 URL，并用回调中的 aeskey 做 AES-256-CBC 解密。
    /// 解密后的图片字节直接以内存方式返回，不落盘。
    /// </summary>
    private async Task<GatewayInboundAttachment?> DownloadAndDecryptImageAsync(
        WeComImagePayload image, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, image.Url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var encryptedBytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (encryptedBytes.Length == 0)
            return null;

        var decryptedBytes = DecryptWeComFile(encryptedBytes, image.AesKey);
        var (contentType, _) = DetectImageType(decryptedBytes);

        return new GatewayInboundAttachment(decryptedBytes, contentType);
    }

    /// <summary>
    /// 企业微信文件解密：AESKey 为 Base64 编码的 32 字节密钥，IV 取密钥前 16 字节。
    /// 加密数据使用 AES-256-CBC，PKCS#7 padding 最大可能为 32 字节。
    /// </summary>
    private static byte[] DecryptWeComFile(byte[] encryptedBytes, string aesKey)
    {
        var key = DecodeBase64WithPadding(aesKey);
        if (key.Length != 32)
            throw new InvalidOperationException($"企业微信图片 aeskey 解码后长度无效：{key.Length}。");

        var iv = key[..16];

        // 参考官方 SDK：如果密文长度不是 AES block size 的倍数，补 0 后再解密。
        if (encryptedBytes.Length % 16 != 0)
        {
            var padded = new byte[encryptedBytes.Length + (16 - encryptedBytes.Length % 16)];
            Buffer.BlockCopy(encryptedBytes, 0, padded, 0, encryptedBytes.Length);
            encryptedBytes = padded;
        }

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;

        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
        return RemovePkcs7Padding(decrypted);
    }

    /// <summary>
    /// Base64 解码企业微信 aeskey，兼容缺少 '=' padding 的情况。
    /// </summary>
    private static byte[] DecodeBase64WithPadding(string value)
    {
        var padding = value.Length % 4;
        if (padding > 0)
            value += new string('=', 4 - padding);
        return Convert.FromBase64String(value);
    }

    /// <summary>
    /// 手动移除 PKCS#7 padding。企业微信文件 padding 可到 32 字节。
    /// </summary>
    private static byte[] RemovePkcs7Padding(byte[] bytes)
    {
        if (bytes.Length == 0)
            throw new InvalidOperationException("企业微信图片解密后内容为空。");

        var padLen = bytes[^1];
        if (padLen is < 1 or > 32 || padLen > bytes.Length)
            throw new InvalidOperationException($"企业微信图片 padding 无效：{padLen}。");

        for (var i = bytes.Length - padLen; i < bytes.Length; i++)
        {
            if (bytes[i] != padLen)
                throw new InvalidOperationException("企业微信图片 padding 字节不一致。");
        }

        var result = new byte[bytes.Length - padLen];
        Buffer.BlockCopy(bytes, 0, result, 0, result.Length);
        return result;
    }

    /// <summary>
    /// 根据图片文件头判断媒体类型和扩展名。
    /// </summary>
    private static (string ContentType, string Extension) DetectImageType(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return ("image/png", ".png");
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ("image/jpeg", ".jpg");
        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            return ("image/gif", ".gif");
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return ("image/webp", ".webp");

        return ("image/jpeg", ".jpg");
    }

    /// <summary>
    /// 企业微信图片下载载荷。
    /// </summary>
    private sealed record WeComImagePayload(string Url, string AesKey);

    // ════════════════════════════ 发送回复 ════════════════════════════

    public async ValueTask SendAsync(GatewayOutboundMessage message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.Text))
            return;

        try
        {
            // 优先通过 WebSocket 更新同一条流式回复（回传原 req_id）。
            if (await TrySendViaWsAsync(message, ct))
                return;

            // REST API 不支持更新流；仅在最终完成帧降级为一次完整文本回复。
            if (!message.IsCompleted)
                return;

            if (HasApiCredentials())
            {
                await RefreshAccessTokenAsync(ct);
                await SendViaApiAsync(message.RecipientId, message.Text, ct);
            }
            else
            {
                Log.WeComApiCredentialsMissing(_logger, message.RecipientId);
            }
        }
        catch (Exception ex)
        {
            Log.WeComSendFailed(_logger, message.RecipientId, ex);
        }
    }

    /// <summary>
    /// 尝试通过 WebSocket 回复或更新流式消息（需 24h 内入站上下文，回传原 req_id）。
    /// </summary>
    private async Task<bool> TrySendViaWsAsync(GatewayOutboundMessage message, CancellationToken ct)
    {
        if (!_replyContexts.TryGetValue(message.RecipientId, out var ctx))
            return false;

        // 超过 24h 的上下文无效
        if (DateTimeOffset.UtcNow - ctx.ReceivedAt > TimeSpan.FromHours(24))
        {
            _replyContexts.TryRemove(message.RecipientId, out _);
            return false;
        }

        var body = "\"msgtype\":\"stream\"," +
                   $"\"stream\":{{\"id\":{JsonStr(message.StreamId)},\"finish\":{message.IsCompleted.ToString().ToLowerInvariant()},\"content\":{JsonStr(message.Text)}}}";
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
            ["agentid"] = _config.AgentIdForRestApi,
            ["text"] = new Dictionary<string, object> { ["content"] = text }
        };

        var json = JsonSerializer.Serialize(payload, WeComJsonContext.Default.DictionaryStringObject);
        var url = $"{ApiBase}/cgi-bin/message/send?access_token={_accessToken}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

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
