using System.Text.Json;
using NarutoCode.Desktop.Api.Contracts;
using NarutoCode.Desktop.Api.Serialization;

namespace NarutoCode.Desktop.Api.Tests;

/// <summary>
/// Run SSE DTO 的 Native AOT JSON 协议测试。
/// </summary>
[TestClass]
public sealed class RunEventSerializationTests
{
    /// <summary>
    /// SSE data JSON 必须使用 Renderer 协议要求的 camelCase 字段名。
    /// </summary>
    [TestMethod]
    public void Serialize_UsesCamelCaseProtocolNames()
    {
        var dto = new RunEventDto(
            "run-1",
            7,
            "message.delta",
            DateTimeOffset.UnixEpoch,
            "hello",
            "Content",
            null,
            null);

        var json = JsonSerializer.Serialize(
            dto,
            DesktopApiJsonSerializerContext.Default.RunEventDto);

        StringAssert.Contains(json, "\"runId\":\"run-1\"");
        StringAssert.Contains(json, "\"sequence\":7");
        StringAssert.Contains(json, "\"eventType\":\"message.delta\"");
        StringAssert.Contains(json, "\"content\":\"hello\"");
        Assert.IsFalse(json.Contains("\"Sequence\"", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("\"EventType\"", StringComparison.Ordinal));
    }
}
