using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Notifications;

public static class MarkNotificationRead
{
    public static void MapMarkNotificationReadEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPut("/api/v1/notifications/{id}/read", async (Guid id, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, context.User.GetUserId());
            await bus.InvokeAsync(command);
            return Results.NoContent();
        })
        .WithName("MarkNotificationRead")
        .WithTags("Notifications")
        .WithSummary("Mark a notification as read")
        .WithDescription("Marks a single notification as read by setting its ReadAt timestamp. Already-read notifications are unaffected.")
        .Produces(204)
        .RequireAuthorization();
    }

    public record Command(Guid NotificationId, Guid UserId);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var notification = await dbContext.Notifications
                .FirstOrDefaultAsync(n => n.Id == request.NotificationId && n.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Notification not found");

            notification.IsRead = true;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogDebug("Notification {NotificationId} marked as read", request.NotificationId);
        }
    }
}
