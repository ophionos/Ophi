using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Alerts;

public static class DeleteAlert
{
    public record Command(Guid AlertId, Guid UserId);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var alert = await dbContext.Alerts
                .FirstOrDefaultAsync(a => a.Id == request.AlertId && a.UserId == request.UserId, cancellationToken) ?? throw new NotFoundException("Alert not found");

            dbContext.Alerts.Remove(alert);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Alert {AlertId} deleted", request.AlertId);
        }
    }

    public static void MapDeleteAlertEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapDelete("/api/v1/alerts/{id:guid}", async (Guid id, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, context.User.GetUserId());
            await bus.InvokeAsync(command);
            return Results.NoContent();
        })
        .WithName("DeleteAlert")
        .WithTags("Alerts")
        .WithSummary("Delete an alert")
        .WithDescription("Permanently deletes an alert. No further notifications will be sent for this alert condition.")
        .Produces(204)
        .RequireAuthorization();
}
