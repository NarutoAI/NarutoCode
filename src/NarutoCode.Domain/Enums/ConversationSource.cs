namespace NarutoCode.Domain.Enums;

/// <summary>
/// 会话来源类型，用于区分本地终端/桌面端创建的会话和外部通道（如企业微信）创建的会话。
/// 通道类会话不在 TUI 和桌面端显示，仅通过对应通道查询。
/// </summary>
public enum ConversationSource
{
    /// <summary>
    /// 本地会话：由 TUI 或桌面端创建，在界面上正常显示。
    /// </summary>
    Local = 0,

    /// <summary>
    /// 企业微信会话：由网关的企业微信通道创建，不在 TUI 和桌面端显示。
    /// </summary>
    WeCom = 1
}
