using NarutoCode.Infrastructure.AIAgents.SubAgents;

namespace NarutoCode.Infrastructure.Tests.AIAgents.SubAgents;

/// <summary>
/// 验证子 Agent 配置按根工作目录隔离加载与校验。
/// </summary>
[TestClass]
public sealed class SubAgentRegistryTests
{
    /// <summary>
    /// 配置文件不存在时，注册表应保持为空并使用默认委派限制。
    /// </summary>
    [TestMethod]
    public async Task InitializeAsync_WhenConfigurationIsMissing_ExposesNoSubAgents()
    {
        var path = Path.Combine(Path.GetTempPath(), $"narutocode-subagents-{Guid.NewGuid():N}.json");
        var registry = new SubAgentRegistry(path);

        await registry.InitializeAsync();

        Assert.IsEmpty(registry.GetAvailableAgents("/workspace/root"));
    }

    /// <summary>
    /// 根工作目录只能获得精确绑定的子 Agent，不得将相似路径视为匹配。
    /// </summary>
    [TestMethod]
    public async Task InitializeAsync_WhenWorkspaceMatchesExactly_ReturnsOnlyItsSubAgents()
    {
        var path = await WriteJsonAsync("""
            {
              "workspaces": [
                {
                  "workspace": "/workspace/root",
                  "subAgents": [
                    {
                      "id": "reviewer",
                      "name": "代码审查",
                      "description": "审查代码质量",
                      "workspace": "/workspace/review"
                    }
                  ]
                },
                {
                  "workspace": "/workspace/root-child",
                  "subAgents": [
                    {
                      "id": "other",
                      "name": "其他",
                      "description": "处理其他任务",
                      "workspace": "/workspace/other"
                    }
                  ]
                }
              ]
            }
            """);
        try
        {
            var registry = new SubAgentRegistry(path);

            await registry.InitializeAsync();

            var agents = registry.GetAvailableAgents("/workspace/root");
            Assert.HasCount(1, agents);
            Assert.AreEqual("reviewer", agents[0].Id);
            Assert.AreEqual("/workspace/review", agents[0].Workspace);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 同一根工作目录下的子 Agent 标识必须唯一，避免模型调用出现歧义。
    /// </summary>
    [TestMethod]
    public async Task InitializeAsync_WhenAgentIdsRepeatWithinOneRootWorkspace_ThrowsInvalidOperationException()
    {
        var path = await WriteJsonAsync("""
            {
              "workspaces": [
                {
                  "workspace": "/workspace/root",
                  "subAgents": [
                    { "id": "reviewer", "name": "一", "description": "一", "workspace": "/workspace/a" },
                    { "id": "reviewer", "name": "二", "description": "二", "workspace": "/workspace/b" }
                  ]
                }
              ]
            }
            """);
        try
        {
            var registry = new SubAgentRegistry(path);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => registry.InitializeAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 创建临时子 Agent 配置文件。
    /// </summary>
    private static async Task<string> WriteJsonAsync(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"narutocode-subagents-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, json);
        return path;
    }
}
