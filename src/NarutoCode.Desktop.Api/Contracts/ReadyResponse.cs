namespace NarutoCode.Desktop.Api.Contracts;

/// <summary>
/// 启动就绪响应，写入 stdout 供 Electron Main 读取端口。
/// </summary>
/// <param name="Type">消息类型，固定为 ready。</param>
/// <param name="Port">实际监听端口。</param>
public sealed record ReadyResponse(string Type, int Port);
