using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Notifications;

public static class MarkAllNotificationsRead
{
    public static void MapMarkAllNotificationsReadEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/notifications/read-all", async (IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(context.User.GetUserId());
            await bus.InvokeAsync(command);
            return Results.NoContent();
        })
        .WithName("MarkAllNotificationsRead")
        .WithTags("Notifications")
        .WithSummary("Mark all notifications as read")
        .WithDescription("Marks all unread notifications for the current user as read in a single operation.")
        .Produces(204)
        .RequireAuthorization();
    }

    public record Command(Guid UserId);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            await dbContext.Notifications
                .Where(n => n.UserId == request.UserId && !n.IsRead)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), cancellationToken);

            logger.LogDebug("All notifications marked as read for user {UserId}", request.UserId);
        }
    }
}
