using Microsoft.Extensions.AI;
using NarutoCode.Domain.Configurations;

namespace NarutoCode.Infrastructure.ChatClients;

/// <summary>
/// 聊天客户端创建工厂，用于按 LLM 协议创建对应的 <see cref="IChatClient" />。
/// </summary>
public interface IChatClientFactory
{
    /// <summary>
    /// 根据 LLM 模型配置创建聊天客户端。
    /// </summary>
    /// <param name="configuration">LLM 模型配置。</param>
    /// <returns>聊天客户端。</returns>
    IChatClient Create(LlmConfiguration configuration);
}