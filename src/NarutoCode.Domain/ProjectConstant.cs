namespace NarutoCode.Domain;

public class ProjectConstant
{
    public static string AppDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ConfigurationDirectory);
    /// <summary>
    /// 程序配置根目录。
    /// </summary>
    public const string ConfigurationDirectory = ".narutocode";

    /// <summary>
    /// 程序数据目录。
    /// </summary>
    public const string DataDirectory = "data";

    /// <summary>
    /// 临时
    /// </summary>
    public const string TempDirectory = "tmp";
    /// <summary>
    /// 配置文件名称。
    /// </summary>
    public const string ConfigurationFileName = "config.json";

    /// <summary>
    /// 运行时设置文件名称。
    /// </summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>
    /// 工作目录子 Agent 编排配置文件名称。
    /// </summary>
    public const string SubAgentsConfigurationFileName = "subagents.json";

    /// <summary>
    /// Agent 模式状态目录名（位于数据目录下，每个会话一个 JSON 文件，按会话 id 命名）。
    /// </summary>
    public const string AgentModeStatesDirectoryName = "agent-modes";
    
    /// <summary>
    /// 
    /// </summary>
    public static string SkillsDirectory =Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agents","skills");
}
