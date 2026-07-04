using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders;

/// <summary>
/// 代码审核
/// </summary>
public class CodeReviewAIContextProvider : AIContextProvider
{
 #pragma warning disable MEAI001
    private readonly AIAgent aiAgent;

    /// <summary>
    /// 本地shell工具
    /// </summary>
    private readonly LocalShellExecutor _persistentShell =
        new(new() {Mode = ShellMode.Persistent, AcknowledgeUnsafe = true});

    public CodeReviewAIContextProvider(IChatClient chatClient, AIContextProvider[] aiContextProviders)
    {
        aiAgent = chatClient.AsAIAgent(new ChatClientAgentOptions()
            {
                ChatOptions = new ChatOptions()
                {
                    Tools = [_persistentShell.AsAIFunction(requireApproval: false)]
                },
                AIContextProviders = aiContextProviders,
            }).AsBuilder()
#pragma warning disable MAAI001
            .UseToolApproval(new ToolApprovalAgentOptions
            {
                AutoApprovalRules = [ToolApprovalAgent.AllToolsAutoApprovalRule] //todo 暂时设置所有工具调用开启自动审批
            })
            .Build();
#pragma warning restore MAAI001
    }

    private static string BuildPrompt(string workGoal) => $$"""
                                                           # Code Review Agent

                                                           你是一个专业的代码审核 Agent。

                                                           你的任务是根据主 Agent 提供的“本次工作目标”，审核当前 Git 仓库中的未提交代码变更。

                                                           ## 本次工作目标

                                                           {{workGoal}}

                                                           ## 审核对象

                                                           只审核当前 Git 未提交变更，包括：

                                                           - staged 变更
                                                           - unstaged 变更
                                                           - untracked 新文件

                                                           不得审核历史代码问题，不得修改任何文件。

                                                           ## 必须执行的只读检查

                                                           你必须先获取并分析：

                                                           1. git status --porcelain
                                                           2. git diff --stat
                                                           3. git diff
                                                           4. git diff --cached --stat
                                                           5. git diff --cached

                                                           如果没有获取到有效 diff，不得给出通过结论。

                                                           ## 审核维度

                                                           1. 目标完成度，权重 35%
                                                           2. 代码质量，权重 25%
                                                           3. 安全风险，权重 20%
                                                           4. 性能影响，权重 10%
                                                           5. 架构设计，权重 10%

                                                           ## 输出格式

                                                           必须输出 Markdown：

                                                           # Code Review Report

                                                           ## 1. 审核结论

                                                           - 门禁结果：Passed | Warning | Failed
                                                           - 综合评分：x/100
                                                           - 是否建议提交：可以提交 | 建议修复后提交 | 不建议提交
                                                           - 本次工作目标完成度：x/100
                                                           - 阻塞问题数量：x
                                                           - Critical 数量：x
                                                           - Error 数量：x
                                                           - Warning 数量：x
                                                           - Info 数量：x

                                                           ## 2. 本次工作目标理解

                                                           用 2-5 条总结你理解的工作目标。

                                                           ## 3. 变更摘要

                                                           | 文件 | 变更类型 | 是否与目标相关 | 说明 |
                                                           |---|---|---|---|

                                                           ## 4. 评分概览

                                                           | 维度 | 得分 | 权重 | 加权分 | 说明 |
                                                           |---|---:|---:|---:|---|
                                                           | 目标完成度 | x/100 | 35% | x | x |
                                                           | 代码质量 | x/100 | 25% | x | x |
                                                           | 安全风险 | x/100 | 20% | x | x |
                                                           | 性能影响 | x/100 | 10% | x | x |
                                                           | 架构设计 | x/100 | 10% | x | x |

                                                           ## 5. 问题详情

                                                           按以下分类输出：

                                                           - 目标完成度问题
                                                           - 代码质量问题
                                                           - 安全风险问题
                                                           - 性能问题
                                                           - 架构设计问题

                                                           每个问题必须包含：

                                                           - 文件
                                                           - 行号
                                                           - 严重级别
                                                           - 扣分
                                                           - 问题描述
                                                           - 为什么影响本次目标
                                                           - diff 依据
                                                           - 修复建议

                                                           ## 6. 必须修复项

                                                           列出阻塞提交的问题。没有则写“无”。

                                                           ## 7. 建议修复项

                                                           列出非阻塞优化项。没有则写“无”。

                                                           ## 8. 总结

                                                           用 2-5 句话说明本次代码是否完成目标、是否建议提交、下一步应该处理什么。

                                                           ## 禁止行为

                                                           - 不得修改任何文件
                                                           - 不得执行 git add / git commit / git reset / git checkout / git clean
                                                           - 不得执行删除文件、安装依赖、格式化、自动修复命令
                                                           - 不得审核与未提交 diff 无关的历史问题
                                                           - 不得把已删除代码问题计入评分
                                                           - 不得在没有查看 diff 的情况下给出通过结论
                                                           """;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new AIContext
        {
            Tools = this._tools ??= this.CreateTools()
        });
    }

    private AITool[]? _tools;

    private AITool[] CreateTools()
    {
        return
        [
            AIFunctionFactory.Create(CodeReview, name: "code_review")
        ];
    }

    [Description(
        "基于 git status 和 git diff 对当前项目的增量变更进行代码审核。工具会分析本次修改内容，从代码质量、安全漏洞、性能问题、架构设计四个维度生成 Markdown 审核报告，并给出 0-100 分的综合评分。该工具只读取 Git 代码变更内容，不修改任何代码文件。")]
    private async Task<string> CodeReview([Description("本轮任务的工作目标")] string workContent)
    {
        var res = await aiAgent.RunAsync(BuildPrompt(workContent));
        return res.Text;
    }
#pragma warning restore MEAI001
}