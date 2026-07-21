using System.Collections.Concurrent;

namespace NarutoCode.Gateway.Channels.WeCom;

/// <summary>
/// 基于 ConcurrentDictionary 的轻量消息去重器。
/// 以 msgid 为键，TTL 过期后自动清理，避免企业微信重复回调。
/// </summary>
public sealed class WeComMessageDedup
{
    /// <summary>去重条目存活时间：5 分钟。</summary>
    private const long TtlMs = 5L * 60 * 1_000;

    /// <summary>最大缓存条目数，超过后触发惰性清理。</summary>
    private const int MaxSize = 2_000;

    private readonly ConcurrentDictionary<string, long> _seen = new(StringComparer.Ordinal);

    /// <summary>
    /// 尝试认领消息，首次返回 true，重复返回 false。
    /// </summary>
    /// <param name="messageId">企业微信消息 msgid。</param>
    /// <returns>是否为首次到达。</returns>
    public bool TryClaim(string messageId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (_seen.TryGetValue(messageId, out var expMs) && expMs > now)
            return false;

        _seen[messageId] = now + TtlMs;

        // 超过上限时惰性清理过期条目
        if (_seen.Count > MaxSize)
            EvictExpired(now);

        return true;
    }

    private void EvictExpired(long now)
    {
        foreach (var key in _seen.Keys)
        {
            if (_seen.TryGetValue(key, out var expMs) && expMs <= now)
                _seen.TryRemove(key, out _);
        }
    }
}
