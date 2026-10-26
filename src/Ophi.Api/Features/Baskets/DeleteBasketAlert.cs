using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Baskets;

public static class DeleteBasketAlert
{
    public record Command(Guid BasketId, Guid AlertId, Guid UserId);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var alert = await dbContext.BasketAlerts
                .FirstOrDefaultAsync(a => a.Id == request.AlertId && a.BasketId == request.BasketId && a.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Basket alert not found");

            dbContext.BasketAlerts.Remove(alert);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A claim (xmin) changed the row after it was read. The delete still stands.
                dbContext.ChangeTracker.Clear();
                await dbContext.BasketAlerts.Where(a => a.Id == alert.Id).ExecuteDeleteAsync(cancellationToken);
            }

            logger.LogInformation("Basket alert {BasketAlertId} deleted", request.AlertId);
        }
    }

    public static void MapDeleteBasketAlertEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapDelete("/api/v1/baskets/{id:guid}/alerts/{alertId:guid}", async (Guid id, Guid alertId, IMessageBus bus, HttpContext context) =>
        {
            await bus.InvokeAsync(new Command(id, alertId, context.User.GetUserId()));
            return Results.NoContent();
        })
        .WithName("DeleteBasketAlert")
        .WithTags("Baskets")
        .WithSummary("Delete a basket alert")
        .WithDescription("Permanently deletes a basket alert. Returns 404 for an alert of another user or another basket.")
        .Produces(204)
        .RequireAuthorization();
}
