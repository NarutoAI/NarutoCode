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

    public static AppConfiguration Config => config ?? throw new InvalidOperationException("程序配置尚未初始化。");

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

        if (configuration?.Llm is null)
        {
            throw new InvalidOperationException("程序配置文件缺少 llm 配置节点。");
        }

        configuration.System ??= new SystemConfiguration();

        if (string.IsNullOrWhiteSpace(configuration.Llm.Provider))
        {
            throw new InvalidOperationException("模型厂商未填写。");
        }

        if (string.IsNullOrWhiteSpace(configuration.Llm.Protocol))
        {
            throw new InvalidOperationException("模型协议未填写。");
        }

        if (string.IsNullOrWhiteSpace(configuration.Llm.Address))
        {
            throw new InvalidOperationException("模型地址未填写。");
        }

        if (string.IsNullOrWhiteSpace(configuration.Llm.ApiKey))
        {
            throw new InvalidOperationException("模型 ApiKey 未填写。");
        }

        if (string.IsNullOrWhiteSpace(configuration.Llm.Model))
        {
            throw new InvalidOperationException("模型名称未填写。");
        }

        config = configuration;
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