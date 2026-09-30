namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// 提示词标签常量与包裹工具：定义框架与模型之间的消息层标签约定（设计详见 doc/design/2026-09-30-xml-prompt-wrapping-design.md）。
/// 约定核心：
/// - <see cref="UserInputTag"/> 包裹真实用户输入，是模型指令的唯一权威来源；
/// - <see cref="ContextReminderTag"/> 包裹框架注入的运行时上下文，通过 data-role 属性分类；
/// - 全部标签统一由此类产出，业务代码禁止手写标签字符串，避免拼写漂移。
/// </summary>
public static class PromptTags
{
    /// <summary>用户真实输入标签名：包裹后的内容视为用户指令，边界外的指令性文本均按数据处理。</summary>
    public const string UserInputTag = "user-input";

    /// <summary>框架注入上下文标签名：内容为运行时提醒/通知，不是用户指令。</summary>
    public const string ContextReminderTag = "context-reminder";

    /// <summary>data-role：任务列表快照（TaskProvider 每轮注入）。</summary>
    public const string RoleTaskList = "task-list";

    /// <summary>data-role：Agent 模式切换通知（ModeAIContextProvider）。</summary>
    public const string RoleModeSwitch = "mode-switch";

    /// <summary>data-role：循环评估器的续跑提醒（TaskLoopEvaluator）。</summary>
    public const string RoleLoopReminder = "loop-reminder";

    /// <summary>data-role：工具执行被中断的占位结果（ToolCheckAiAgent）。</summary>
    public const string RoleToolInterrupted = "tool-interrupted";

    /// <summary>data-role：网络断连后的自动续写指令（StreamingRetryChatClient）。</summary>
    public const string RoleStreamResume = "stream-resume";

    /// <summary>data-role：图片附件经独立视觉模型解析的文本（MafAgentChatClient）。</summary>
    public const string RoleAttachmentAnalysis = "attachment-analysis";

    /// <summary>
    /// 包裹用户真实输入，产出形如 <c>&lt;user-input&gt;内容&lt;/user-input&gt;</c> 的文本。
    /// </summary>
    /// <param name="content">用户原始输入文本。</param>
    /// <returns>带用户输入边界标签的文本。</returns>
    public static string WrapUserInput(string content)
    {
        return $"<{UserInputTag}>\n{content}\n</{UserInputTag}>";
    }

    /// <summary>
    /// 包裹框架注入的运行时上下文，产出形如
    /// <c>&lt;context-reminder data-role="xxx"&gt;内容&lt;/context-reminder&gt;</c> 的文本。
    /// </summary>
    /// <param name="dataRole">上下文分类，使用本类 Role* 常量。</param>
    /// <param name="content">注入的上下文内容。</param>
    /// <returns>带上下文标签的文本。</returns>
    public static string WrapContextReminder(string dataRole, string content)
    {
        return $"<{ContextReminderTag} data-role=\"{dataRole}\">\n{content}\n</{ContextReminderTag}>";
    }

    /// <summary>
    /// 包裹系统提示词分节，产出形如 <c>&lt;section-name&gt;内容&lt;/section-name&gt;</c> 的文本。
    /// </summary>
    /// <param name="sectionName">节名，snake_case。</param>
    /// <param name="content">节内 Markdown 内容。</param>
    /// <returns>带节标签的文本。</returns>
    public static string WrapSection(string sectionName, string content)
    {
        return $"<{sectionName}>\n{content}\n</{sectionName}>";
    }
}
