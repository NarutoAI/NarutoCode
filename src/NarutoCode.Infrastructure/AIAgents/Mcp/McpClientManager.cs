using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using NarutoCode.Domain.Configurations;

namespace NarutoCode.Infrastructure.AIAgents.Mcp;

/// <summary>
/// 管理 MCP 客户端连接，并把远端 MCP tools 转换为当前 Agent 可调用的 AI 工具。
/// </summary>
public sealed class McpClientManager(
    ILogger<McpClientManager> logger,
    ILoggerFactory loggerFactory) : IAsyncDisposable
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly List<McpClient> _clients = [];
    private IReadOnlyList<AITool>? _tools;

    /// <summary>
    /// 获取所有已启用 MCP 服务暴露的工具。
    /// </summary>
    /// <param name="mcpServers">MCP 服务配置集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前 Agent 可调用的 MCP 工具集合。</returns>
    public async ValueTask<IReadOnlyList<AITool>> GetToolsAsync(
        IReadOnlyDictionary<string, McpServerConfiguration> mcpServers,
        CancellationToken cancellationToken = default)
    {
        if (_tools is not null)
        {
            return _tools;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_tools is not null)
            {
                return _tools;
            }

            _tools = await LoadEnabledServerToolsAsync(mcpServers, cancellationToken);
            return _tools;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async Task<IReadOnlyList<AITool>> LoadEnabledServerToolsAsync(
        IReadOnlyDictionary<string, McpServerConfiguration> mcpServers,
        CancellationToken cancellationToken)
    {
        var tools = new List<AITool>();
        foreach (var item in mcpServers.Where(static item => item.Value.Enabled))
        {
            var serverTools = await TryListToolsAsync(item.Key, item.Value, cancellationToken);
            if (serverTools is not null)
            {
                tools.AddRange(serverTools);
            }
        }

        return tools.ToArray();
    }

    /// <summary>
    /// 加载工具信息
    /// </summary>
    /// <param name="serverName"></param>
    /// <param name="configuration"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task<IReadOnlyList<AITool>?> TryListToolsAsync(
        string serverName,
        McpServerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        //参数校验
        if (!TryValidateConfiguration(
                serverName,
                configuration,
                out var safeServerName,
                out var workingDirectory,
                out var httpEndpoint))
        {
            return null;
        }

        McpClient? client = null;
        try
        {
            var transport = CreateTransport(
                safeServerName,
                configuration,
                workingDirectory,
                httpEndpoint);

            client = await McpClient.CreateAsync(
                transport,
                cancellationToken: cancellationToken);

            var serverTools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            var tools = CreateExposedTools(serverName, safeServerName, configuration.Description, serverTools);
            _clients.Add(client);
            return tools;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (client is not null)
            {
                await client.DisposeAsync();
            }

            Log.McpServerToolLoadingFailed(logger, exception, serverName);
            return null;
        }
    }

    /// <summary>
    /// 根据服务配置创建 MCP 客户端传输协议。
    /// </summary>
    /// <param name="safeServerName">用于日志和工具名称的服务名称。</param>
    /// <param name="configuration">已完成校验的服务配置。</param>
    /// <param name="workingDirectory">stdio 服务的工作目录。</param>
    /// <param name="httpEndpoint">HTTP 服务端点。</param>
    /// <returns>MCP 客户端传输协议。</returns>
    private IClientTransport CreateTransport(
        string safeServerName,
        McpServerConfiguration configuration,
        string? workingDirectory,
        Uri? httpEndpoint)
    {
        if (string.Equals(configuration.Type, "stdio", StringComparison.OrdinalIgnoreCase))
        {
            return new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = safeServerName,
                Command = configuration.Command,
                Arguments = configuration.Args,
                WorkingDirectory = workingDirectory,
                EnvironmentVariables = configuration.Env.Count == 0
                    ? null
                    : configuration.Env.ToDictionary(static item => item.Key, static item => (string?)item.Value)
            });
        }

        return new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = safeServerName,
            Endpoint = httpEndpoint!,
            AdditionalHeaders = configuration.Headers.Count == 0
                ? null
                : configuration.Headers
        }, loggerFactory);
    }

    private IReadOnlyList<AITool> CreateExposedTools(
        string serverName,
        string safeServerName,
        string? serverDescription,
        IList<McpClientTool> serverTools)
    {
        var exposedToolNames = new HashSet<string>(StringComparer.Ordinal);
        var tools = new List<AITool>();
        foreach (var tool in serverTools)
        {
            if (TryCreateExposedTool(serverName, safeServerName, serverDescription, tool, exposedToolNames,
                    out var exposedTool))
            {
                tools.Add(exposedTool);
            }
        }

        return tools.ToArray();
    }

    /// <summary>
    /// 创建工具，然后替换名称
    /// </summary>
    /// <param name="serverName"></param>
    /// <param name="safeServerName"></param>
    /// <param name="serverDescription">MCP 服务用途描述。</param>
    /// <param name="tool">MCP 服务原始工具。</param>
    /// <param name="exposedToolNames">已暴露工具名集合。</param>
    /// <param name="exposedTool">暴露给 Agent 的工具。</param>
    private bool TryCreateExposedTool(
        string serverName,
        string safeServerName,
        string? serverDescription,
        McpClientTool tool,
        HashSet<string> exposedToolNames,
        out AITool exposedTool)
    {
        exposedTool = null!;
        var exposedToolName = $"{safeServerName}__{tool.Name}";
        if (!exposedToolNames.Add(exposedToolName))
        {
            Log.DuplicateMcpToolNameSkipped(logger, serverName, exposedToolName);
            return false;
        }

        var namedTool = tool.WithName(exposedToolName);
        exposedTool = string.IsNullOrWhiteSpace(serverDescription)
            ? namedTool
            : namedTool.WithDescription(BuildToolDescription(serverDescription, tool.Description));
        return true;
    }

    /// <summary>
    /// 构建工具的描述信息
    /// 允许用户自己增加工具描述的前缀，用于同一个工具在不同的工作目录下 AI的识别问题
    /// </summary>
    /// <param name="serverDescription"></param>
    /// <param name="toolDescription"></param>
    /// <returns></returns>
    private static string BuildToolDescription(string serverDescription, string toolDescription)
    {
        if (string.IsNullOrWhiteSpace(toolDescription))
        {
            return serverDescription.Trim();
        }

        return $"{serverDescription.Trim()}\n\n{toolDescription}";
    }

    private bool TryValidateConfiguration(
        string serverName,
        McpServerConfiguration configuration,
        out string safeServerName,
        out string? workingDirectory,
        out Uri? httpEndpoint)
    {
        safeServerName = serverName.Trim();
        workingDirectory = null;
        httpEndpoint = null;

        if (string.Equals(configuration.Type, "http", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(configuration.Url, UriKind.Absolute, out httpEndpoint) ||
                (httpEndpoint.Scheme != Uri.UriSchemeHttp && httpEndpoint.Scheme != Uri.UriSchemeHttps))
            {
                Log.InvalidMcpHttpServerUrl(logger, serverName, configuration.Url);
                return false;
            }

            return true;
        }

        if (!string.Equals(configuration.Type, "stdio", StringComparison.OrdinalIgnoreCase))
        {
            Log.UnsupportedMcpServerType(logger, serverName, configuration.Type);
            return false;
        }

        if (string.IsNullOrWhiteSpace(configuration.Command))
        {
            Log.MissingMcpServerCommand(logger, serverName);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(configuration.WorkingDirectory))
        {
            workingDirectory = Path.GetFullPath(configuration.WorkingDirectory);
            if (!Directory.Exists(workingDirectory))
            {
                Log.McpServerWorkingDirectoryNotFound(logger, serverName, workingDirectory);
                return false;
            }
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        _initializationLock.Dispose();
    }
}