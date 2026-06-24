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
    /// 
    /// </summary>
    public static string SkillsDirectory =Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agents","skills");
}
