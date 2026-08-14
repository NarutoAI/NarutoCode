using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NarutoCode.Domain;

namespace NarutoCode.Infrastructure.AIAgents.DelegatingChatClients;

/// <summary>
/// 流式请求传输层重试客户端：在"尚未收到任何响应内容"时静默重试整个请求；
/// 若已收到纯文本内容后断连，则把已输出内容回填为 assistant 上下文并追加续写指令后重发，
/// 让模型从断点继续输出。整个重试对上层（Agent / UI）表现为一条连续完整的流。
/// 一旦本次请求已经产出工具调用 / 错误等不可续写内容，则不重试并向上抛出，避免重复副作用。
/// </summary>
public class StreamingRetryChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    /// <summary>
    /// 单个请求允许的自动重试次数（不含首次尝试）。
    /// </summary>
    private const int MaxRetryAttempts = 2;

    /// <summary>
    /// 重试前的等待时间，按尝试次数递增，避免刚断开立即重连再次失败。
    /// </summary>
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];

    /// <summary>
    /// 续写指令：要求模型基于回填的半截回复继续输出，避免重新组织开头导致重复。
    /// </summary>
    private const string ResumeInstruction = "请从刚才的断点继续回答，直接续写，不要重复已生成内容。";

    private static readonly Lazy<ILogger?> Logger = new(ResolveLogger);

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 物化消息列表：IEnumerable 可能只可枚举一次，重试时需要复用同一批消息
        var messageList = messages.ToList();

        for (var attempt = 0; ; attempt++)
        {
            // 收集本轮已产出的 update，用于重试前判定与续写上下文回填
            var collected = new List<ChatResponseUpdate>();
            Exception? streamingException = null;

            // 手动枚举而不是 await foreach：yield 不允许出现在 try 块内
            await using (var enumerator =
                         base.GetStreamingResponseAsync(messageList, options, cancellationToken)
                             .GetAsyncEnumerator(cancellationToken))
            {
                while (true)
                {
                    try
                    {
                        if (!await enumerator.MoveNextAsync())
                        {
                            break;
                        }
                    }
                    catch (Exception exception) when (TransportExceptionDetector.IsTransportDisconnect(exception))
                    {
                        streamingException = exception;
                        break;
                    }

                    var update = enumerator.Current;
                    collected.Add(update);
                    yield return update;
                }
            }

            if (streamingException is null)
            {
                // 流正常结束，退出重试循环
                yield break;
            }

            // 已产出不可续写内容（工具调用/错误等）、超过重试上限或用户取消时，不再重试并向上抛出由上层处理
            if (attempt >= MaxRetryAttempts
                || cancellationToken.IsCancellationRequested
                || !CanRetryWith(collected))
            {
                ExceptionDispatchInfo.Capture(streamingException).Throw();
            }

            if (Logger.Value is { } logger)
            {
                // 源生成器日志：异常作为 Exception 参数自动记录，模板占位符避免字符串插值
                Log.StreamingRequestRetrying(
                    logger,
                    streamingException,
                    attempt + 1,
                    collected.Count,
                    RetryDelays[attempt].TotalSeconds);
            }

            // 等待退避时间后重试；取消时 Task.Delay 抛出 OperationCanceledException 完成传播
            await Task.Delay(RetryDelays[attempt], cancellationToken);

            // 已产出正式文本时构造续写上下文（半截回复回填 + 续写指令），否则保持原消息静默重试
            var partialText = CollectPartialText(collected);
            if (partialText.Length > 0)
            {
                messageList = BuildResumeMessages(messageList, partialText);
            }
        }
    }

    /// <summary>
    /// 判断已收集的内容是否允许自动续写重试。
    /// </summary>
    /// <param name="collected">本轮已产出的流式更新。</param>
    /// <returns>仅包含纯文本 / 思考 / 用量内容时返回 <see langword="true" />；出现工具调用、错误、图片等不可续写内容时返回 <see langword="false" />。</returns>
    private static bool CanRetryWith(IReadOnlyList<ChatResponseUpdate> collected)
    {
        foreach (var update in collected)
        {
            if (update.Contents is null)
            {
                continue;
            }

            foreach (var content in update.Contents)
            {
                // 仅允许文本类内容参与续写；其它内容（工具调用、错误、图片等）回填会导致请求失败或重复副作用
                if (content is TextContent or TextReasoningContent or UsageContent)
                {
                    continue;
                }

                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 汇总已产出的正式文本（不含 thinking），用于回填续写上下文。
    /// </summary>
    /// <param name="collected">本轮已产出的流式更新。</param>
    /// <returns>正式输出文本拼接结果。</returns>
    private static string CollectPartialText(IReadOnlyList<ChatResponseUpdate> collected)
    {
        var builder = new StringBuilder();
        foreach (var update in collected)
        {
            if (update.Contents is null)
            {
                continue;
            }

            foreach (var content in update.Contents)
            {
                if (content is TextContent {Text: { } text} && !string.IsNullOrEmpty(text))
                {
                    builder.Append(text);
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// 构造续写请求消息序列：原消息 + 半截 assistant 回复 + 续写指令（user）。
    /// 以 user 结尾满足 Chat Completions 对消息序列的约束，同时让模型基于自身已输出内容继续生成。
    /// </summary>
    /// <param name="messageList">原始请求消息列表（已物化）。</param>
    /// <param name="partialText">断连前已输出的正式文本。</param>
    /// <returns>续写请求使用的消息列表。</returns>
    private static List<ChatMessage> BuildResumeMessages(List<ChatMessage> messageList, string partialText)
    {
        var resumeMessages = new List<ChatMessage>(messageList.Count + 2);
        // 原请求消息（含历史与用户最新指令）保持不变
        resumeMessages.AddRange(messageList);
        // 回填半截回复，让模型知道此前已输出到哪里
        resumeMessages.Add(new ChatMessage(ChatRole.Assistant, partialText));
        // 追加续写指令并以 user 收尾，满足接口消息序列约束
        resumeMessages.Add(new ChatMessage(ChatRole.User, ResumeInstruction));

        return resumeMessages;
    }

    /// <summary>
    /// 从根服务提供者解析日志器；非依赖注入创建场景（如测试）解析失败时返回 null 静默重试。
    /// </summary>
    private static ILogger? ResolveLogger()
    {
        try
        {
            return RootServiceProviderLocator.ServiceProvider
                ?.GetService<ILoggerFactory>()
                ?.CreateLogger<StreamingRetryChatClient>();
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// 流式重试客户端的 ChatClientBuilder 扩展。
/// </summary>
public static class StreamingRetryChatClientExtensions
{
    /// <summary>
    /// 为 ChatClient 管道注册传输层流式重试能力，应放在管道最内层（紧贴真实客户端），
    /// 这样重试只重发底层 HTTP 请求，不会重放上层管道的副作用逻辑。
    /// </summary>
    /// <param name="builder">ChatClient 管道构建器。</param>
    /// <returns>原构建器，支持链式调用。</returns>
    public static ChatClientBuilder UseStreamingRetry(this ChatClientBuilder builder)
    {
        return builder.Use(innerClient => new StreamingRetryChatClient(innerClient));
    }
}
