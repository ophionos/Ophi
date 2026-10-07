using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Entities;
using Ophi.Domain.Extensions;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Wolverine;

namespace Ophi.Api.Features.Alerts;

/// <summary>
/// Pauses or resumes an alert. Scoped to the <c>active</c> flag only — not a general
/// <c>UpdateAlert</c>. A target change is still delete + create, and a dormant alert's target goes
/// through <see cref="RedenominateAlert"/>.
/// </summary>
public static class SetAlertActive
{
    public record Request(bool Active);

    public record Command(Guid AlertId, bool Active)
    {
        public Guid UserId { get; init; }
    }

    public class Handler(OphiDbContext dbContext, IOptions<AlertSettings> alertSettings, ILogger<Handler> logger)
    {
        private readonly AlertSettings _alertSettings = alertSettings.Value;

        public async Task<GetAlerts.Dto> Handle(Command request, CancellationToken cancellationToken)
        {
            var alert = await dbContext.Alerts
                .Include(a => a.Product)
                .FirstOrDefaultAsync(
                    a => a.Id == request.AlertId && a.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Alert not found");

            if (alert.IsActive != request.Active)
            {
                if (request.Active)
                {
                    // CreateAlert caps *active* alerts. Resume must hold the same line, or
                    // pause -> create -> resume walks straight past it.
                    var activeCount = await dbContext.Alerts
                        .CountAsync(a => a.UserId == request.UserId && a.IsActive, cancellationToken);
                    if (activeCount >= _alertSettings.MaxAlertsPerUser)
                    {
                        throw new ApiException(
                            $"Maximum number of active alerts ({_alertSettings.MaxAlertsPerUser}) reached.",
                            422, "MaxAlertsReached");
                    }
                }

                await SaveWithOneRetryAsync(alert, request.Active, cancellationToken);
                logger.LogInformation(
                    "Alert {AlertId} {State}", alert.Id, request.Active ? "resumed" : "paused");
            }

            var product = alert.Product;
            return new GetAlerts.Dto(
                alert.Id,
                alert.ProductId,
                product.Name,
                product.CurrentPrice,
                alert.TargetPrice,
                alert.Condition.ToApiString(),
                alert.IsActive,
                alert.LastTriggeredAt,
                alert.Currency,
                product.Currency,
                alert.HasCurrencyMismatch(product.Currency));
        }

        /// <summary>
        /// <c>Alert</c> carries a Postgres xmin concurrency token, and <c>CheckAlertsHandler</c>'s
        /// <c>LastTriggeredAt</c> save is its fire claim, so the two writers can collide. The flag
        /// is idempotent and does not conflict with a claim, so reload and reapply once; a second
        /// conflict means the row is hot and the user can retry. (If this save wins instead, the
        /// checker's claim loses and the paused alert does not fire — the right outcome.)
        /// </summary>
        private async Task SaveWithOneRetryAsync(Alert alert, bool active, CancellationToken cancellationToken)
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
                    throw new ApiException(
                        "This alert changed while it was being updated. Try again.", 409, "Conflict");
                }
            }
        }

        private static void Apply(Alert alert, bool active)
        {
            if (active) alert.Resume();
            else alert.Pause();
        }
    }

    public static void MapSetAlertActiveEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPatch("/api/v1/alerts/{id:guid}",
            async (Guid id, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, request.Active) { UserId = context.User.GetUserId() };
            var result = await bus.InvokeAsync<GetAlerts.Dto>(command);
            return Results.Ok(result);
        })
        .WithName("SetAlertActive")
        .WithTags("Alerts")
        .WithSummary("Pause or resume an alert")
        .WithDescription(
            "Sets whether an alert can fire. Trigger history is kept. Resuming counts against the " +
            "per-user active-alert limit (422 MaxAlertsReached). Only the active flag can be changed.")
        .Produces<GetAlerts.Dto>(200)
        .RequireAuthorization();
}
