using System.Text.Json;
using NarutoCode.Domain.Configurations;

namespace NarutoCode.Domain;

/// <summary>
/// 程序配置。
/// </summary>
public static class AppData
{
    private static AppConfiguration? config;

    private static string ConfigurationFilePath => BuildDefaultConfigurationFilePath();

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
        configuration.McpServers ??= [];
        EnsureLlmConfigurationsExists(configuration.Llms);
        ValidateLlmConfigurations(configuration.Llms);
        ValidateVisionConfiguration(configuration.Vision);

        config = configuration;
    }

    /// <summary>
    /// 清除当前进程内缓存的配置，供测试宿主切换临时配置目录。
    /// </summary>
    internal static void ResetForTesting()
    {
        config = null;
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

    /// <summary>
    /// 校验视觉模型配置：显式启用时必填字段必须齐全，避免运行期才暴露配置缺失。
    /// </summary>
    /// <param name="vision">视觉模型配置；为 null 或未启用时跳过校验。</param>
    private static void ValidateVisionConfiguration(VisionConfiguration? vision)
    {
        if (vision is not { Enabled: true })
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(vision.Address))
        {
            throw new InvalidOperationException("视觉模型配置 vision.address 未填写。");
        }

        if (string.IsNullOrWhiteSpace(vision.ApiKey))
        {
            throw new InvalidOperationException("视觉模型配置 vision.apiKey 未填写。");
        }

        if (string.IsNullOrWhiteSpace(vision.Model))
        {
            throw new InvalidOperationException("视觉模型配置 vision.model 未填写。");
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
