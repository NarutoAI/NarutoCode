using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace NarutoCode.Infrastructure.Tests.CodeGraph;

[TestClass]
public class CodeGraphMCPTest
{
    [TestMethod]
    [TestCategory("Integration")]
    public async Task Connection_Test()
    {
        if (!TryResolveCodeGraphCommand(out var codeGraphCommand))
        {
            Assert.Inconclusive("未找到 codegraph 可执行文件。设置 CODEGRAPH_PATH 或安装 codegraph 后可运行该集成测试。");
        }

        await using var mcpClient = await McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Name = "CodeGraph",
            Command = codeGraphCommand,
            Arguments = ["serve", "--mcp"],
        }));

        var toolsAsync = await mcpClient.ListToolsAsync();
        var toolNames = toolsAsync.Select(tool => tool.Name).ToArray();
        var prefixedToolNames = toolsAsync.Select(tool => tool.WithName($"codegraph__{tool.Name}").Name).ToArray();

        Console.WriteLine($"CodeGraph tools: {string.Join(", ", toolNames)}");
        Assert.IsNotEmpty(toolNames, "CodeGraph MCP server did not return any tools.");
        CollectionAssert.Contains(toolNames, "codegraph_explore");
        CollectionAssert.Contains(prefixedToolNames, "codegraph__codegraph_explore");
    }

    /// <summary>
    /// 尝试解析 CodeGraph 可执行文件路径，避免未安装外部命令时导致常规测试失败。
    /// </summary>
    /// <param name="command">解析到的 CodeGraph 命令。</param>
    /// <returns>找到可执行文件时返回 <see langword="true" />。</returns>
    private static bool TryResolveCodeGraphCommand(out string command)
    {
        var configuredPath = Environment.GetEnvironmentVariable("CODEGRAPH_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            command = configuredPath;
            return true;
        }

        var localBinPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "codegraph");
        if (File.Exists(localBinPath))
        {
            command = localBinPath;
            return true;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, "codegraph");
            if (File.Exists(candidate))
            {
                command = candidate;
                return true;
            }
        }

        command = string.Empty;
        return false;
    }
}
