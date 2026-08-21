using System.Runtime.InteropServices;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// 核心身份指令贡献者：贡献 Harness 主指令（架构师人设、沟通准则、工作目录、安全红线与项目 AGENTS.md）。
/// </summary>
public sealed class CoreInstructionsContributor : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "CoreInstructions";

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        // 读取工作目录下的 AGENTS.md 作为项目信息追加到主指令末尾，不存在时为空串
        var agentMd = ReadAgentsMd(context.WorkingDirectory);

        builder.AddInstruction($"""
            你是一位强大的软件架构师和产品专家。

            ## 沟通准则
            - 行动前理解意图、定位代码、规划最小改动并验证。
            - 保持简洁直接，仅在必要时澄清。
            - 修改已有文件前先阅读，遵守项目现有命名、格式和模式。
            - 完成后简要说明结果和验证状态。

            ## 工作目录地址
            - {context.WorkingDirectory}

            ## 其它信息
            - 当前操作系统：`{RuntimeInformation.OSDescription}`
            - 除非用户明确要求，否则必须使用中文回复。

            ## 安全红线
            - 未获当前对话明确授权时，不得修改系统目录、全局配置目录、凭据目录或其它敏感路径。
            - 仅在当前工作目录或用户明确指定的项目目录中进行文件操作。

            {agentMd}
            """);
    }

    /// <summary>
    /// 读取工作目录下的 AGENTS.md，不存在时返回空字符串。
    /// </summary>
    /// <param name="workingDirectory">当前工作目录。</param>
    /// <returns>项目信息指令片段。</returns>
    private static string ReadAgentsMd(string workingDirectory)
    {
        var agentPath = Path.Combine(workingDirectory, "AGENTS.md");
        return File.Exists(agentPath) ? $"## 项目信息\n{File.ReadAllText(agentPath)}" : string.Empty;
    }
}
