using System.Globalization;
using System.Text.Json;
using NarutoCode.Domain.Messages;
using NarutoCode.Desktop.Api.Contracts;
using NarutoCode.Desktop.Api.Runs;
using NarutoCode.Desktop.Api.Serialization;

namespace NarutoCode.Desktop.Api.Endpoints;

/// <summary>
/// Run 启动、SSE 流、审批和取消端点。
/// </summary>
internal static class RunEndpoints
{
    // 支持的图片媒体类型
    private static readonly HashSet<string> SupportedImageMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/webp", "image/gif"
    };

    /// <summary>
    /// 注册 Run 端点。
    /// </summary>
    public static WebApplication MapRunEndpoints(this WebApplication app)
    {
        // 启动 Run
        app.MapPost("/api/v1/conversations/{conversationId}/runs", async (
            string conversationId,
            StartRunRequest request,
            IDesktopRunCoordinator coordinator,
            CancellationToken ct) =>
        {
            if (!long.TryParse(conversationId, CultureInfo.InvariantCulture, out var id))
            {
                return Results.BadRequest(new { code = "invalid_conversation_id", message = "会话 ID 格式无效。" });
            }

            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return Results.BadRequest(new { code = "invalid_content", message = "消息内容不能为空。" });
            }

            // 校验附件
            List<AgentMessageAttachment>? attachments = null;
            if (request.Attachments is { Count: > 0 })
            {
                attachments = [];
                foreach (var att in request.Attachments)
                {
                    if (!File.Exists(att.Path))
                    {
                        return Results.UnprocessableEntity(new { code = "invalid_attachment", message = $"附件文件不存在：{att.Path}" });
                    }

                    if (!SupportedImageMediaTypes.Contains(att.MediaType))
                    {
                        return Results.BadRequest(new { code = "invalid_media_type", message = $"不支持的媒体类型：{att.MediaType}" });
                    }

                    attachments.Add(new AgentMessageAttachment(att.Path, att.MediaType));
                }
            }

            var message = new AgentMessage(
                AgentMessageType.Content,
                request.Content,
                attachments: attachments);

            var run = await coordinator.StartAsync(new ConversationSessionId(id), message, ct);
            var response = new StartRunResponse(
                run.RunId,
                run.Status.ToString().ToLowerInvariant(),
                $"/api/v1/conversations/{conversationId}/runs/{run.RunId}/events");
            return Results.Ok(response);
        });

        // SSE 事件流
        app.MapGet("/api/v1/conversations/{conversationId}/runs/{runId}/events", async (
            string conversationId,
            string runId,
            HttpContext context,
            IDesktopRunCoordinator coordinator,
            CancellationToken ct) =>
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            try
            {
                await foreach (var item in coordinator.ReadEventsAsync(runId, ct))
                {
                    var dto = RunEventDtoExtensions.From(item);
                    // 写入 SSE 帧：id / event / data
                    await context.Response.WriteAsync($"id: {dto.Sequence}\n", ct);
                    await context.Response.WriteAsync($"event: {dto.EventType}\n", ct);
                    await context.Response.WriteAsync("data: ", ct);
                    await JsonSerializer.SerializeAsync(
                        context.Response.Body,
                        dto,
                        DesktopApiJsonSerializerContext.Default.RunEventDto,
                        ct);
                    await context.Response.WriteAsync("\n\n", ct);
                    await context.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 客户端断开连接，正常退出
            }

            return Results.Empty;
        });

        // 解决工具审批
        app.MapPost("/api/v1/runs/{runId}/approvals/{approvalId}", async (
            string runId,
            string approvalId,
            ResolveApprovalRequest request,
            IDesktopRunCoordinator coordinator,
            CancellationToken ct) =>
        {
            await coordinator.ResolveApprovalAsync(runId, approvalId, request.Approved, ct);
            return Results.Ok(new { resolved = true });
        });

        // 取消 Run
        app.MapPost("/api/v1/runs/{runId}/cancel", async (
            string runId,
            IDesktopRunCoordinator coordinator,
            CancellationToken ct) =>
        {
            await coordinator.CancelAsync(runId, ct);
            return Results.NoContent();
        });

        return app;
    }
}

/// <summary>
/// RunEventDto 工厂扩展。
/// </summary>
internal static class RunEventDtoExtensions
{
    /// <summary>
    /// 将 RunEvent 转换为 SSE 序列化 DTO。
    /// </summary>
    public static RunEventDto From(RunEvent evt)
    {
        return new RunEventDto(
            evt.RunId,
            evt.Sequence,
            evt.EventType,
            evt.Timestamp,
            evt.Message?.Content,
            evt.Message?.Type.ToString(),
            evt.Message?.ToolApprovalContent,
            evt.ApprovalId);
    }
}
