using System.Text.Json;
using NarutoCode.Domain.Configurations;

namespace NarutoCode.Infrastructure.Tests.Mcp;

[TestClass]
public sealed class McpConfigurationTests
{
    [TestMethod]
    public void Deserialize_McpServers_ConfiguresStdioServer()
    {
        const string json = """
                            {
                              "llms": [],
                              "mcpServers": {
                                "codegraph": {
                                  "type": "stdio",
                                  "command": "codegraph",
                                  "args": ["serve", "--mcp"],
                                  "description": "用于分析当前后端代码仓库的 CodeGraph 服务。",
                                  "workingDirectory": "/tmp/codegraph-workspace",
                                  "enabled": true
                                }
                              }
                            }
                            """;

        var configuration = JsonSerializer.Deserialize<AppConfiguration>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.IsNotNull(configuration);
        Assert.IsTrue(configuration.McpServers.ContainsKey("codegraph"));

        var server = configuration.McpServers["codegraph"];
        Assert.AreEqual("stdio", server.Type);
        Assert.AreEqual("codegraph", server.Command);
        CollectionAssert.AreEqual(new[] { "serve", "--mcp" }, server.Args);
        Assert.AreEqual("用于分析当前后端代码仓库的 CodeGraph 服务。", server.Description);
        Assert.AreEqual("/tmp/codegraph-workspace", server.WorkingDirectory);
        Assert.IsTrue(server.Enabled);
    }

    [TestMethod]
    public void Deserialize_McpServers_ConfiguresHttpServer()
    {
        const string json = """
                            {
                              "llms": [],
                              "mcpServers": {
                                "remote-tools": {
                                  "type": "http",
                                  "url": "https://example.com/mcp",
                                  "headers": {
                                    "Authorization": "Bearer example-token",
                                    "X-Tenant-Id": "naruto"
                                  },
                                  "description": "远端 MCP 工具服务。",
                                  "enabled": true
                                }
                              }
                            }
                            """;

        var configuration = JsonSerializer.Deserialize<AppConfiguration>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.IsNotNull(configuration);
        Assert.IsTrue(configuration.McpServers.ContainsKey("remote-tools"));

        var server = configuration.McpServers["remote-tools"];
        Assert.AreEqual("http", server.Type);
        Assert.AreEqual("https://example.com/mcp", server.Url);
        Assert.AreEqual("Bearer example-token", server.Headers["Authorization"]);
        Assert.AreEqual("naruto", server.Headers["X-Tenant-Id"]);
        Assert.AreEqual("远端 MCP 工具服务。", server.Description);
        Assert.IsTrue(server.Enabled);
    }
}
