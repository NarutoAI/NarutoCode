using System.Net;
using System.Text.Json;
using NarutoCode.Desktop.Api.Contracts;

namespace NarutoCode.Desktop.Api.Tests;

/// <summary>
/// 健康检查端点和 Bearer 认证测试。
/// </summary>
[TestClass]
public sealed class HealthEndpointTests
{
    /// <summary>
    /// 缺少令牌时返回 401。
    /// </summary>
    [TestMethod]
    public async Task Health_WhenTokenIsMissing_ReturnsUnauthorized()
    {
        await using var factory = new DesktopApiFactory();
        var response = await factory.CreateClient().GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// 携带正确令牌时返回 200 和健康状态。
    /// </summary>
    [TestMethod]
    public async Task Health_WhenTokenIsValid_ReturnsOk()
    {
        await using var factory = new DesktopApiFactory();
        var client = factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(json.Contains("\"status\":\"ok\"") || json.Contains("\"status\": \"ok\""),
            $"响应应包含 status=ok，实际：{json}");
    }
}
