using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Notifications;

public static class GetNotifications
{
    public static void MapGetNotificationsEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/notifications", async (bool? unreadOnly, int? page, int? pageSize, IMessageBus bus, HttpContext context) =>
        {
            var query = new Query(context.User.GetUserId(), unreadOnly ?? false, page, pageSize);
            var result = await bus.InvokeAsync<Response>(query);
            return Results.Ok(result);
        })
        .WithName("GetNotifications")
        .WithTags("Notifications")
        .WithSummary("List notifications")
        .WithDescription("Returns recent notifications for the current user, ordered by creation date descending. Includes price alerts, scrape failures, and system messages. Paginated with configurable page size.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Query(Guid UserId, bool UnreadOnly, int? Page = null, int? PageSize = null);

    public record Response(List<NotificationDto> Items, int Total, int Page, int PageSize);

    public record NotificationDto(
        Guid Id,
        string Title,
        string Message,
        string Type,
        bool IsRead,
        Guid? ProductId,
        string? ProductName,
        DateTime CreatedAt);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching notifications for user {UserId} (unreadOnly={UnreadOnly})", request.UserId, request.UnreadOnly);

            var page = Math.Max(request.Page ?? 1, 1);
            var pageSize = Math.Clamp(request.PageSize ?? PaginationDefaults.DefaultPageSize, 1, PaginationDefaults.MaxPageSize);

            var query = dbContext.Notifications
                .AsNoTracking()
                .Where(n => n.UserId == request.UserId);

            if (request.UnreadOnly)
            {
                query = query.Where(n => !n.IsRead);
            }

            var total = await query.CountAsync(cancellationToken);

            var notifications = await query
                .OrderByDescending(n => n.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(n => new NotificationDto(
                    n.Id,
                    n.Title,
                    n.Message,
                    n.Type.ToApiString(),
                    n.IsRead,
                    n.ProductId,
                    n.Product != null ? n.Product.Name : null,
                    n.CreatedAt))
                .ToListAsync(cancellationToken);

            logger.LogDebug("Returning {Count}/{Total} notifications for user {UserId}", notifications.Count, total, request.UserId);
            return new Response(notifications, total, page, pageSize);
        }
    }
}
