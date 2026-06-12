using System.ComponentModel;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders;

/// <summary>
/// 为 Agent 提供 SVG 预览渲染能力。
/// </summary>
public sealed partial  class SvgRenderProvider : AIContextProvider
{
    /// <summary>
    /// 单个 SVG 最大字节数，避免模型生成超大内容导致磁盘和上下文压力。
    /// </summary>
    private const int MaxSvgBytes = 512 * 1024;

    /// <summary>
    /// SVG 预览文件输出目录名称。
    /// </summary>
    private const string PreviewDirectoryName = "svg-previews";

    /// <summary>
    /// SVG 工具提示词。
    /// </summary>
    private const string Instructions =
        """
        ## SVG 渲染

        你可以使用 `VisualizeShowWidget` 工具渲染 SVG 图形。该工具会把 SVG 写入当前工作区的 `.narutocode/svg-previews` 目录，并生成一个安全隔离的 HTML 预览文件。

        使用规则：
        - 仅当用户明确要求图形、图表、流程图、架构图、状态图、视觉预览或需要展示 SVG 时使用。
        - `widgetCode` 必须是完整 SVG，必须以 <svg 开始，不要传入 Markdown 代码围栏。
        - 当前工具只支持 SVG，不支持任意 HTML；如果需要 HTML 交互组件，应先说明当前终端版本只提供静态 SVG 预览。
        - SVG 内容应尽量自包含，不依赖外部网络资源。
        - 不要在 SVG 中包含 script、foreignObject、onload 等事件属性、javascript: 链接或外部 URL等非法元素。
        - 生成结果后，向用户说明 SVG 文件路径和 HTML 预览路径。
        - 不要把大段 SVG 源码直接输出给用户，除非用户明确要求查看源码。
        """;

    private readonly string _workspaceDirectory;
    private readonly AITool[] _tools;

    /// <summary>
    /// 创建 SVG 渲染上下文提供器。
    /// </summary>
    /// <param name="workspaceDirectory">当前工作区目录。</param>
    public SvgRenderProvider(string workspaceDirectory)
    {
        if (string.IsNullOrWhiteSpace(workspaceDirectory))
        {
            throw new ArgumentException("工作区目录不能为空。", nameof(workspaceDirectory));
        }

        this._workspaceDirectory = Path.GetFullPath(workspaceDirectory);
        _tools = [AIFunctionFactory.Create(VisualizeShowWidget, serializerOptions: AIContentJsonSerializerContext.Default.Options)];
    }

    /// <inheritdoc />
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new AIContext
        {
            Instructions = Instructions,
            Tools = _tools
        });
    }

    /// <summary>
    /// 渲染 SVG 图形并生成安全隔离的 HTML 预览文件。
    /// </summary>
    /// <param name="title">预览标题。</param>
    /// <param name="widgetCode">完整 SVG 源码，必须以 svg 根元素开始。</param>
    /// <param name="loadingMessages">渲染时展示的加载提示，当前 TUI 版本仅记录到元数据。</param>
    /// <returns>结构化 JSON 工具结果，包含 SVG 文件路径和 HTML 预览路径。</returns>
    [Description("渲染 SVG 图形，生成 .svg 文件和安全隔离的 HTML 预览文件")]
    private async Task<string> VisualizeShowWidget(
        [Description("预览标题，例如 架构图 或 任务流程图")]
        string title,
        [Description("完整 SVG 源码，必须以 <svg 开始，不要包含 Markdown 代码围栏")]
        string widgetCode,
        [Description("渲染时展示的加载提示，当前 TUI 版本仅记录到元数据")]
        string[]? loadingMessages = null)
    {
        var normalizedTitle = NormalizeTitle(title);
        var svg = NormalizeSvg(widgetCode);
        var validationError = ValidateSvg(svg);
        if (validationError is not null)
        {
            return Serialize(new SvgRenderToolResult
            {
                Success = false,
                Error = validationError
            });
        }

        var outputDirectory = CreatePreviewDirectory(normalizedTitle, svg);
        var svgPath = Path.Combine(outputDirectory, "widget.svg");
        var htmlPath = Path.Combine(outputDirectory, "preview.html");
        var metadataPath = Path.Combine(outputDirectory, "metadata.json");

        await File.WriteAllTextAsync(svgPath, svg, Encoding.UTF8);
        await File.WriteAllTextAsync(htmlPath, BuildPreviewDocument(normalizedTitle, svg), Encoding.UTF8);
        await File.WriteAllTextAsync(
            metadataPath,
            SerializeIndented(new SvgRenderMetadataResult
            {
                Title = normalizedTitle,
                CreatedAt = DateTimeOffset.UtcNow,
                LoadingMessages = loadingMessages ?? [],
                SvgPath = svgPath,
                PreviewHtmlPath = htmlPath
            }),
            Encoding.UTF8);

        return Serialize(new SvgRenderToolResult
        {
            Success = true,
            Title = normalizedTitle,
            SvgPath = svgPath,
            PreviewHtmlPath = htmlPath,
            PreviewUrl = new Uri(htmlPath).AbsoluteUri,
            MetadataPath = metadataPath,
            Message = "SVG 预览已生成。当前 TUI 不直接执行 SVG/HTML，使用浏览器打开 preview_html_path 可查看安全隔离预览。"
        });
    }

    /// <summary>
    /// 规范化预览标题。
    /// </summary>
    /// <param name="title">用户传入标题。</param>
    /// <returns>非空标题。</returns>
    private static string NormalizeTitle(string title)
    {
        return string.IsNullOrWhiteSpace(title) ? "svg-preview" : title.Trim();
    }

    /// <summary>
    /// 规范化 SVG 源码，移除模型可能附带的 Markdown 代码围栏。
    /// </summary>
    /// <param name="widgetCode">原始 SVG 源码。</param>
    /// <returns>规范化后的 SVG 源码。</returns>
    private static string NormalizeSvg(string widgetCode)
    {
        var trimmed = widgetCode.Trim();
        var match = SvgCodeFenceRegex().Match(trimmed);
        return match.Success ? match.Groups[1].Value.Trim() : trimmed;
    }

    /// <summary>
    /// 校验 SVG 内容，拒绝脚本、事件属性和外部资源。
    /// </summary>
    /// <param name="svg">SVG 源码。</param>
    /// <returns>错误信息；校验通过时返回 null。</returns>
    private static string? ValidateSvg(string svg)
    {
        if (string.IsNullOrWhiteSpace(svg))
        {
            return "SVG 内容不能为空。";
        }

        if (!svg.TrimStart().StartsWith("<svg", StringComparison.OrdinalIgnoreCase))
        {
            return "widgetCode 必须是完整 SVG，并以 <svg 开始。";
        }

        if (Encoding.UTF8.GetByteCount(svg) > MaxSvgBytes)
        {
            return $"SVG 内容过大，最大允许 {MaxSvgBytes} 字节。";
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true,
            IgnoreComments = false
        };

        try
        {
            using var stringReader = new StringReader(svg);
            using var reader = XmlReader.Create(stringReader, settings);
            var hasRootSvg = false;
            while (reader.Read())
            {
                // 只检查元素和属性，避免用字符串匹配误伤普通文本内容。
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (!hasRootSvg)
                {
                    if (!reader.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase))
                    {
                        return "SVG 根元素必须是 <svg>。";
                    }

                    hasRootSvg = true;
                }

                if (IsBlockedElement(reader.LocalName))
                {
                    return $"SVG 不允许包含 <{reader.LocalName}> 元素。";
                }

                if (!reader.HasAttributes)
                {
                    continue;
                }

                while (reader.MoveToNextAttribute())
                {
                    if (IsBlockedAttribute(reader.LocalName, reader.Value))
                    {
                        return $"SVG 属性 {reader.LocalName} 包含不安全内容。";
                    }
                }

                reader.MoveToElement();
            }

            return hasRootSvg ? null : "未找到 SVG 根元素。";
        }
        catch (XmlException exception)
        {
            return $"SVG XML 格式无效: {exception.Message}";
        }
    }

    /// <summary>
    /// 判断元素是否属于不允许的高风险 SVG 元素。
    /// </summary>
    /// <param name="elementName">元素名称。</param>
    /// <returns>高风险元素返回 true。</returns>
    private static bool IsBlockedElement(string elementName)
    {
        return elementName.Equals("script", StringComparison.OrdinalIgnoreCase)
               || elementName.Equals("foreignObject", StringComparison.OrdinalIgnoreCase)
               || elementName.Equals("iframe", StringComparison.OrdinalIgnoreCase)
               || elementName.Equals("object", StringComparison.OrdinalIgnoreCase)
               || elementName.Equals("embed", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断属性是否包含事件处理器、脚本协议或外部资源引用。
    /// </summary>
    /// <param name="attributeName">属性名称。</param>
    /// <param name="attributeValue">属性值。</param>
    /// <returns>不安全属性返回 true。</returns>
    private static bool IsBlockedAttribute(string attributeName, string attributeValue)
    {
        if (attributeName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedValue = attributeValue.Trim();
        return normalizedValue.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
               || normalizedValue.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
               || normalizedValue.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
               || normalizedValue.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || normalizedValue.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 创建本次 SVG 预览输出目录。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="svg">SVG 源码。</param>
    /// <returns>输出目录路径。</returns>
    private string CreatePreviewDirectory(string title, string svg)
    {
        var root = Path.Combine(_workspaceDirectory, ".narutocode", PreviewDirectoryName);
        Directory.CreateDirectory(root);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var slug = Slugify(title);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(svg)))[..8].ToLowerInvariant();
        var directory = Path.Combine(root, $"{timestamp}_{slug}_{hash}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// 构建安全隔离的 HTML 预览文档。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="svg">SVG 源码。</param>
    /// <returns>HTML 文档。</returns>
    private static string BuildPreviewDocument(string title, string svg)
    {
        var escapedTitle = WebUtility.HtmlEncode(title);
        var escapedSvg = WebUtility.HtmlEncode(svg);
        return $$"""
               <!DOCTYPE html>
               <html lang="zh-CN">
               <head>
                 <meta charset="utf-8">
                 <meta name="viewport" content="width=device-width, initial-scale=1">
                 <title>{{escapedTitle}}</title>
                 <style>
                   :root { color-scheme: light dark; }
                   body {
                     margin: 0;
                     min-height: 100vh;
                     display: grid;
                     grid-template-rows: auto 1fr;
                     background: #0f172a;
                     color: #e2e8f0;
                     font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
                   }
                   header {
                     padding: 12px 16px;
                     border-bottom: 1px solid rgba(148, 163, 184, 0.25);
                   }
                   main {
                     padding: 16px;
                     overflow: auto;
                   }
                   iframe {
                     width: 100%;
                     min-height: calc(100vh - 96px);
                     border: 1px solid rgba(148, 163, 184, 0.35);
                     border-radius: 12px;
                     background: white;
                   }
                 </style>
               </head>
               <body>
                 <header>{{escapedTitle}}</header>
                 <main>
                   <iframe sandbox="" srcdoc="{{escapedSvg}}" title="{{escapedTitle}}"></iframe>
                 </main>
               </body>
               </html>
               """;
    }

    /// <summary>
    /// 将标题转换为适合文件夹名称的短标识。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <returns>文件夹名称片段。</returns>
    private static string Slugify(string title)
    {
        var slug = SlugRegex().Replace(title.ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "svg" : slug[..Math.Min(slug.Length, 40)];
    }

    /// <summary>
    /// 序列化工具结果。
    /// </summary>
    private static string Serialize(SvgRenderToolResult value)
    {
        return JsonSerializer.Serialize(value, AIContentJsonSerializerContext.Default.SvgRenderToolResult);
    }

    /// <summary>
    /// 按缩进格式序列化元数据。
    /// </summary>
    private static string SerializeIndented(SvgRenderMetadataResult value)
    {
        var options = new JsonSerializerOptions(AIContentJsonSerializerContext.Default.Options)
        {
            WriteIndented = true
        };
        return JsonSerializer.Serialize(value, new AIContentJsonSerializerContext(options).SvgRenderMetadataResult);
    }

    [GeneratedRegex("^```(?:svg|xml)?\\s*([\\s\\S]*?)\\s*```$", RegexOptions.IgnoreCase)]
    private static partial Regex SvgCodeFenceRegex();

    [GeneratedRegex("[^a-z0-9一-龥]+", RegexOptions.IgnoreCase)]
    private static partial Regex SlugRegex();
}

/// <summary>
/// SVG 渲染工具返回结果。
/// </summary>
internal sealed class SvgRenderToolResult
{
    /// <summary>
    /// 渲染是否成功。
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// 错误信息。
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>
    /// 预览标题。
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>
    /// SVG 文件路径。
    /// </summary>
    [JsonPropertyName("svg_path")]
    public string? SvgPath { get; init; }

    /// <summary>
    /// HTML 预览文件路径。
    /// </summary>
    [JsonPropertyName("preview_html_path")]
    public string? PreviewHtmlPath { get; init; }

    /// <summary>
    /// HTML 预览文件 URL。
    /// </summary>
    [JsonPropertyName("preview_url")]
    public string? PreviewUrl { get; init; }

    /// <summary>
    /// 元数据文件路径。
    /// </summary>
    [JsonPropertyName("metadata_path")]
    public string? MetadataPath { get; init; }

    /// <summary>
    /// 面向 Agent 的结果消息。
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// SVG 渲染元数据文件内容。
/// </summary>
internal sealed class SvgRenderMetadataResult
{
    /// <summary>
    /// 预览标题。
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// 创建时间。
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// 加载提示列表。
    /// </summary>
    [JsonPropertyName("loading_messages")]
    public string[] LoadingMessages { get; init; } = [];

    /// <summary>
    /// SVG 文件路径。
    /// </summary>
    [JsonPropertyName("svg_path")]
    public required string SvgPath { get; init; }

    /// <summary>
    /// HTML 预览文件路径。
    /// </summary>
    [JsonPropertyName("preview_html_path")]
    public required string PreviewHtmlPath { get; init; }
}
