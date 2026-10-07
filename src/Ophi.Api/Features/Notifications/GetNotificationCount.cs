using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Notifications;

public static class GetNotificationCount
{
    public static void MapGetNotificationCountEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/notifications/count", async (IMessageBus bus, HttpContext context) =>
        {
            var query = new Query(context.User.GetUserId());
            var result = await bus.InvokeAsync<Response>(query);
            return Results.Ok(result);
        })
        .WithName("GetNotificationCount")
        .WithTags("Notifications")
        .WithSummary("Get unread notification count")
        .WithDescription("Returns the count of unread notifications for the current user. Used by the frontend notification bell indicator.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Query(Guid UserId);

    public record Response(int Unread);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching unread notification count for user {UserId}", request.UserId);

            var unreadCount = await dbContext.Notifications
                .CountAsync(n => n.UserId == request.UserId && !n.IsRead, cancellationToken);

            logger.LogDebug("User {UserId} has {UnreadCount} unread notifications", request.UserId, unreadCount);
            return new Response(unreadCount);
        }
    }
}
