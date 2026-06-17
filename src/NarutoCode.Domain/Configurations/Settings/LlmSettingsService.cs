using System.Text.Json;
using NarutoCode.Domain.LlmContextAccessors;

namespace NarutoCode.Domain.Configurations.Settings;

/// <summary>
/// 运行时配置相关
/// </summary>
public sealed class LlmSettingsService : ILlmSettingsService
{
    private readonly ILlmContextAccessor _llmContextAccessor;
    private readonly string _settingsFilePath;

    public LlmSettingsService(ILlmContextAccessor llmContextAccessor)
    {
        this._llmContextAccessor = llmContextAccessor ?? throw new ArgumentNullException(nameof(llmContextAccessor));
        _settingsFilePath = Path.Combine(ProjectConstant.AppDirectory, ProjectConstant.SettingsFileName);
        //初始化配置
        InitializeSettings();
    }

    public string CurrentProvider =>
        _llmContextAccessor.Current?.Provider ?? ValidProvider(ReadSettings().Provider);


    public LlmConfiguration CurrentLlm => ResolveLlm(CurrentProvider);


    public IReadOnlyList<string> GetAvailableProviders()
    {
        return AppData.Config.Llms.Select(item => item.Provider).ToArray();
    }

    /// <summary>
    /// 切换模型上下文
    /// </summary>
    /// <param name="provider"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void SwitchProvider(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new InvalidOperationException("provider 不能为空。");
        }

        var llm = AppData.Config.Llms.FirstOrDefault(item =>
            string.Equals(item.Provider, provider, StringComparison.OrdinalIgnoreCase));
        if (llm is null)
        {
            throw new InvalidOperationException($"provider 不存在：{provider}");
        }

        var settings = ReadSettings();
        settings.Provider = llm.Provider;
        SaveSetting(settings);
        SetCurrentProvider(llm.Provider);
    }

    private void InitializeSettings()
    {
        var settings = ReadSettings();
        settings.Provider = ValidProvider(settings.Provider);
        SaveSetting(settings);
        SetCurrentProvider(settings.Provider);
    }

    private AppSettings ReadSettings()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new AppSettings();
        }

        using var stream = File.OpenRead(_settingsFilePath);
        return JsonSerializer.Deserialize(stream, AppConfigurationContext.Default.AppSettings) ?? new AppSettings();
    }

    /// <summary>
    /// 校验名称
    /// </summary>
    /// <param name="provider"></param>
    /// <returns></returns>
    private static string ValidProvider(string? provider)
    {
        //第一个作为默认的
        var defaultProvider = AppData.Config.Llms[0].Provider;
        if (string.IsNullOrWhiteSpace(provider))
        {
            return defaultProvider;
        }

        var llm = AppData.Config.Llms.FirstOrDefault(item =>
            string.Equals(item.Provider, provider, StringComparison.OrdinalIgnoreCase));
        return llm?.Provider ?? defaultProvider;
    }

    private static LlmConfiguration ResolveLlm(string provider)
    {
        return AppData.Config.Llms.FirstOrDefault(item =>
                   string.Equals(item.Provider, provider, StringComparison.OrdinalIgnoreCase))
               ?? AppData.Config.Llms[0];
    }

    /// <summary>
    /// 更新 配置
    /// </summary>
    /// <param name="settings"></param>
    private void SaveSetting(AppSettings settings)
    {
        Directory.CreateDirectory(ProjectConstant.AppDirectory);
        if (File.Exists(_settingsFilePath))
        {
            //移除老的
            File.Delete(_settingsFilePath);
        }

        //更新
        using var stream = new FileStream(
            _settingsFilePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        JsonSerializer.Serialize(
            stream,
            settings,
            AppConfigurationContext.Default.AppSettings);
    }

    private void SetCurrentProvider(string provider)
    {
        _llmContextAccessor.Current = new LlmContext {Provider = provider};
    }
}