using System.Globalization;
using NarutoCode.Domain.Conversations;
using NarutoCode.Desktop.Api.Contracts;
using NarutoCode.Desktop.Api.Workspaces;

namespace NarutoCode.Desktop.Api.Endpoints;

/// <summary>
/// 工作区相关端点。
/// </summary>
internal static class WorkspaceEndpoints
{
    /// <summary>
    /// 注册工作区端点。
    /// </summary>
    public static WebApplication MapWorkspaceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/workspaces");

        // 列出所有工作区
        group.MapGet("/", async (IConversationService service, CancellationToken ct) =>
        {
            var summaries = await service.ListWorkspacesAsync(ct);
            var dtos = summaries.Select(s => new WorkspaceSummaryDto(
                WorkspaceId.Create(s.WorkDirectory),
                Path.GetFileName(s.WorkDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                s.WorkDirectory,
                s.LastUpdatedAt,
                s.ConversationCount,
                Directory.Exists(s.WorkDirectory))).ToList();
            return Results.Ok(dtos);
        });

        // 打开（或创建）工作区
        group.MapPost("/", async (OpenWorkspaceRequest request, IConversationService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.WorkDirectory))
            {
                return Results.BadRequest(new { code = "invalid_work_directory", message = "工作目录不能为空。" });
            }

            var result = await service.OpenWorkspaceAsync(request.WorkDirectory, ct);
            var workspaceId = WorkspaceId.Create(request.WorkDirectory);
            var conversationDto = new ConversationSummaryDto(
                result.History.SessionId.Value.ToString(CultureInfo.InvariantCulture),
                "conversation",
                DateTime.UtcNow,
                DateTime.UtcNow,
                0,
                result.History.TokenCount,
                0,
                string.Empty);
            var response = new OpenWorkspaceResponse(workspaceId, conversationDto, result.Created);
            return Results.Ok(response);
        });

        // 列出工作区下的会话
        group.MapGet("/{workspaceId}/conversations", async (
            string workspaceId,
            IConversationService service,
            CancellationToken ct) =>
        {
            // 通过遍历工作区摘要匹配 workspaceId
            var summaries = await service.ListWorkspacesAsync(ct);
            var match = summaries.FirstOrDefault(s => WorkspaceId.Create(s.WorkDirectory) == workspaceId);
            if (match is null)
            {
                return Results.NotFound(new { code = "workspace_not_found", message = $"工作区 {workspaceId} 不存在。" });
            }

            var conversations = await service.ListWorkspaceConversationsAsync(match.WorkDirectory, ct);
            var dtos = conversations.Select(c => new ConversationSummaryDto(
                c.Id.ToString(CultureInfo.InvariantCulture),
                c.Title,
                c.CreatedAt,
                c.UpdatedAt,
                c.MessageCount,
                c.TokenCount,
                c.LastUsageTokenCount,
                c.LastUserMessagePreview)).ToList();
            return Results.Ok(dtos);
        });

        // 在工作区下创建新会话
        group.MapPost("/{workspaceId}/conversations", async (
            string workspaceId,
            IConversationService service,
            CancellationToken ct) =>
        {
            var summaries = await service.ListWorkspacesAsync(ct);
            var match = summaries.FirstOrDefault(s => WorkspaceId.Create(s.WorkDirectory) == workspaceId);
            if (match is null)
            {
                return Results.NotFound(new { code = "workspace_not_found", message = $"工作区 {workspaceId} 不存在。" });
            }

            var history = await service.CreateWorkspaceConversationAsync(match.WorkDirectory, ct);
            return Results.Ok(new ConversationSummaryDto(
                history.SessionId.Value.ToString(CultureInfo.InvariantCulture),
                Path.GetFileName(match.WorkDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                DateTime.UtcNow,
                DateTime.UtcNow,
                0,
                0,
                0,
                string.Empty));
        });

        return app;
    }
}
