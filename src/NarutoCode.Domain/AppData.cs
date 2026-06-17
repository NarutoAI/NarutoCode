using System.Text.Json;
using NarutoCode.Domain.Configurations;

namespace NarutoCode.Domain;

/// <summary>
/// 程序配置。
/// </summary>
public static class AppData
{
    private static readonly string ConfigurationFilePath = BuildDefaultConfigurationFilePath();
    private static AppConfiguration? config;

    /// <summary>
    /// 当前程序配置。
    /// </summary>
    public static AppConfiguration Config => config ?? throw new InvalidOperationException("程序配置尚未初始化。");

    /// <summary>
    /// 初始化程序配置并校验 LLM 配置集合。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步初始化任务。</returns>
    public static async Task InitAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigurationDirectoryExists();

        if (!File.Exists(ConfigurationFilePath))
        {
            throw new FileNotFoundException("程序配置文件不存在。", ConfigurationFilePath);
        }

        await using var stream = new FileStream(
            ConfigurationFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var configuration = await JsonSerializer.DeserializeAsync(
            stream,
            AppConfigurationContext.Default.AppConfiguration,
            cancellationToken);

        if (configuration is null)
        {
            throw new InvalidOperationException("程序配置文件无效。");
        }

        configuration.System ??= new SystemConfiguration();
        EnsureLlmConfigurationsExists(configuration.Llms);
        ValidateLlmConfigurations(configuration.Llms);

        config = configuration;
    }

    private static void EnsureLlmConfigurationsExists(IReadOnlyCollection<LlmConfiguration> llms)
    {
        if (llms.Count == 0)
        {
            throw new InvalidOperationException("程序配置文件缺少 llms 配置节点。");
        }
    }

    private static void ValidateLlmConfigurations(IReadOnlyCollection<LlmConfiguration> llms)
    {
        var providers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var llm in llms)
        {
            index++;
            ValidateLlmConfiguration(llm, index);
            if (!providers.Add(llm.Provider))
            {
                throw new InvalidOperationException($"模型厂商 provider 重复：{llm.Provider}");
            }
        }
    }

    private static void ValidateLlmConfiguration(LlmConfiguration llm, int index)
    {
        if (string.IsNullOrWhiteSpace(llm.Provider))
        {
            throw new InvalidOperationException($"第 {index} 个模型厂商未填写。");
        }

        if (string.IsNullOrWhiteSpace(llm.Protocol))
        {
            throw new InvalidOperationException($"第 {index} 个模型协议未填写。");
        }

        if (string.IsNullOrWhiteSpace(llm.Address))
        {
            throw new InvalidOperationException($"第 {index} 个模型地址未填写。");
        }

        if (string.IsNullOrWhiteSpace(llm.ApiKey))
        {
            throw new InvalidOperationException($"第 {index} 个模型 ApiKey 未填写。");
        }

        if (string.IsNullOrWhiteSpace(llm.Model))
        {
            throw new InvalidOperationException($"第 {index} 个模型名称未填写。");
        }
    }

    private static string BuildDefaultConfigurationFilePath()
    {
        return Path.Combine(
            ProjectConstant.AppDirectory,
            ProjectConstant.ConfigurationFileName);
    }

    private static void EnsureConfigurationDirectoryExists()
    {
        var directory = Path.GetDirectoryName(ConfigurationFilePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
