namespace NarutoCode.Domain.Configurations;

/// <summary>
/// 系统运行配置，描述日志等非模型业务的基础运行参数。
/// </summary>
public sealed class SystemConfiguration
{
    /// <summary>
    /// 日志最小输出级别，支持 Verbose、Debug、Information、Warning、Error、Fatal；未配置时默认 Error。
    /// </summary>
    public string LogLevel { get; set; } = "Error";
}
