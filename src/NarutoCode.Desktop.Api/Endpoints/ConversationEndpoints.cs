using System.Globalization;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Messages;
using NarutoCode.Desktop.Api.Contracts;

namespace NarutoCode.Desktop.Api.Endpoints;

/// <summary>
/// 会话历史端点。
/// </summary>
internal static class ConversationEndpoints
{
    /// <summary>
    /// 注册会话端点。
    /// </summary>
    public static WebApplication MapConversationEndpoints(this WebApplication app)
    {
        // 获取会话历史
        app.MapGet("/api/v1/conversations/{conversationId}", async (
            string conversationId,
            IConversationService service,
            CancellationToken ct) =>
        {
            if (!long.TryParse(conversationId, CultureInfo.InvariantCulture, out var id))
            {
                return Results.BadRequest(new { code = "invalid_conversation_id", message = "会话 ID 格式无效。" });
            }

            var history = await service.LoadConversationHistoryAsync(new ConversationSessionId(id), ct);
            var dto = new ConversationHistoryDto(
                history.SessionId.Value.ToString(CultureInfo.InvariantCulture),
                history.TokenCount,
                history.Messages.Select(m => new ConversationHistoryMessageDto(
                    m.Role.ToString(),
                    m.Message.Type.ToString(),
                    m.Message.Content,
                    m.Message.ToolApprovalContent,
                    m.Message.CreatedAt,
                    [])).ToList());
            return Results.Ok(dto);
        });

        return app;
    }
}
