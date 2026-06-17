using NarutoCode.Domain.Enums;

namespace NarutoCode.Domain.Configurations.Settings;

/// <summary>
///
/// </summary>
public interface ILlmSettingsService
{
    /// <summary>
    /// 当前使用的 provider。
    /// </summary>
    string CurrentProvider { get; }

    /// <summary>
    /// 当前使用的 LLM 配置。
    /// </summary>
    LlmConfiguration CurrentLlm { get; }

    /// <summary>
    /// 当前使用的推理强度。
    /// </summary>
    LlmEffort CurrentEffort { get; }

    /// <summary>
    /// 获取所有可用 provider。
    /// </summary>
    /// <returns>provider 名称集合。</returns>
    IReadOnlyList<string> GetAvailableProviders();

    /// <summary>
    /// 获取所有可用推理强度。
    /// </summary>
    /// <returns>推理强度集合。</returns>
    IReadOnlyList<LlmEffort> GetAvailableEfforts();

    /// <summary>
    /// 切换当前 provider，并保存到 settings.json。
    /// </summary>
    /// <param name="provider">目标 provider。</param>
    void SwitchProvider(string provider);

    /// <summary>
    /// 切换当前推理强度，并保存到 settings.json。
    /// </summary>
    /// <param name="effort">目标推理强度。</param>
    void SwitchEffort(LlmEffort effort);
}
