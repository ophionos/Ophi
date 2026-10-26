using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Wolverine;

namespace Ophi.Api.Features.Baskets;

/// <summary>Pauses or resumes a basket alert. A target change is delete + create, as for product alerts.</summary>
public static class SetBasketAlertActive
{
    public record Request(bool Active);

    public record Command(Guid BasketId, Guid AlertId, bool Active)
    {
        public Guid UserId { get; init; }
    }

    public class Handler(OphiDbContext dbContext, IOptions<AlertSettings> alertSettings, ILogger<Handler> logger)
    {
        private readonly AlertSettings _alertSettings = alertSettings.Value;

        public async Task<BasketAlertDto> Handle(Command request, CancellationToken cancellationToken)
        {
            var alert = await dbContext.BasketAlerts
                .FirstOrDefaultAsync(a => a.Id == request.AlertId && a.BasketId == request.BasketId && a.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Basket alert not found");

            if (alert.IsActive != request.Active)
            {
                // Resume holds the create limit, or pause -> create -> resume walks past it.
                if (request.Active &&
                    await dbContext.BasketAlerts.CountAsync(a => a.UserId == request.UserId && a.IsActive, cancellationToken) >= _alertSettings.MaxAlertsPerUser)
                {
                    throw BasketAlertLimit.Reached(_alertSettings.MaxAlertsPerUser);
                }

                await SaveWithOneRetryAsync(alert, request.Active, cancellationToken);
                logger.LogInformation("Basket alert {BasketAlertId} {State}", alert.Id, request.Active ? "resumed" : "paused");
            }

            var totals = await BasketTotalsReader.ComputeAsync(dbContext, alert.BasketId, request.UserId, cancellationToken);
            return BasketAlertDto.From(alert, totals);
        }

        /// <summary>
        /// The race of <c>SetAlertActive</c>: <c>BasketAlert</c> carries the xmin token and
        /// <c>CheckBasketAlertsHandler</c>'s claim saves under it. The flag is idempotent, so reload and
        /// reapply once; a second conflict is a 409 the user can retry.
        /// </summary>
        private async Task SaveWithOneRetryAsync(BasketAlert alert, bool active, CancellationToken cancellationToken)
        {
            Apply(alert, active);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                await dbContext.Entry(alert).ReloadAsync(cancellationToken);
                Apply(alert, active);
                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new ApiException("This alert changed while it was being updated. Try again.", 409, "Conflict");
                }
            }
        }

        private static void Apply(BasketAlert alert, bool active)
        {
            if (active) alert.Resume();
            else alert.Pause();
        }
    }

    public static void MapSetBasketAlertActiveEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPatch("/api/v1/baskets/{id:guid}/alerts/{alertId:guid}",
            async (Guid id, Guid alertId, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, alertId, request.Active) { UserId = context.User.GetUserId() };
            return Results.Ok(await bus.InvokeAsync<BasketAlertDto>(command));
        })
        .WithName("SetBasketAlertActive")
        .WithTags("Baskets")
        .WithSummary("Pause or resume a basket alert")
        .WithDescription("Sets whether a basket alert can fire. Trigger history is kept. Resuming counts against the active basket-alert limit (422 MaxAlertsReached). Only the active flag can be changed. Returns 404 for an alert of another user or another basket.")
        .Produces<BasketAlertDto>(200)
        .RequireAuthorization();
}
