using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace NarutoCode.Desktop.Api.Tests;

/// <summary>
/// Desktop API 集成测试工厂，创建临时 .narutocode 目录和有效 config.json。
/// </summary>
public sealed class DesktopApiFactory : WebApplicationFactory<Program>
{
    /// <summary>测试用 Bearer 令牌。</summary>
    public const string TestToken = "test-desktop-token-12345";

    private readonly string _tempDirectory;

    /// <summary>
    /// 创建工厂，设置临时配置目录和环境变量。
    /// </summary>
    public DesktopApiFactory()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"narutocode-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        // 写入有效的 config.json（camelCase 格式）
        var configJson = """
        {
          "llms": [
            {
              "provider": "test",
              "protocol": "OpenAIChat",
              "address": "http://localhost:11434",
              "apiKey": "test-key",
              "model": "test-model",
              "maxContextWindowTokens": 8192,
              "maxOutputTokens": 4096
            }
          ],
          "system": {
            "logLevel": "Warning"
          },
          "mcpServers": {},
          "enableApproval": false,
          "maxTurnCount": 10
        }
        """;
        File.WriteAllText(Path.Combine(_tempDirectory, "config.json"), configJson);

        // 设置 Desktop API 环境变量
        Environment.SetEnvironmentVariable("NARUTOCODE_DESKTOP_TOKEN", TestToken);
        Environment.SetEnvironmentVariable("NARUTOCODE_APP_DATA_DIRECTORY", _tempDirectory);
        Environment.SetEnvironmentVariable("NARUTOCODE_DESKTOP_PORT", "0");
    }

    /// <summary>
    /// 创建带认证头的 HttpClient。
    /// </summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestToken);
        return client;
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }

        await base.DisposeAsync();
    }
}
