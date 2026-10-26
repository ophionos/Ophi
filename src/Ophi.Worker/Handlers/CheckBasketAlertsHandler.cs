using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Domain.Messages.Events;
using Ophi.Domain.Services;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Wolverine.Attributes;

namespace Ophi.Worker.Handlers;

/// <summary>
/// Checks the basket alerts (#151) of the baskets that hold the product whose price changed. A change
/// of quantity or of a shipping rate does not run this check; the next price event does.
/// </summary>
[WolverineHandler]
[LocalQueue("events")]
public static class CheckBasketAlertsHandler
{
    public static async Task<IEnumerable<BasketAlertTriggeredEvent>> HandleAsync(
        PriceUpdatedEvent @event,
        OphiDbContext dbContext,
        IOptions<AlertSettings> alertSettings,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cooldown = TimeSpan.FromMinutes(alertSettings.Value.CooldownMinutes);

        var alerts = (await dbContext.BasketAlerts
                .Where(a => a.IsActive &&
                            dbContext.BasketItems.Any(i => i.BasketId == a.BasketId && i.ProductId == @event.ProductId))
                .ToListAsync(cancellationToken))
            .Where(a => !a.IsInCooldown(cooldown, now))
            .ToList();
        if (alerts.Count == 0) return [];

        var triggered = new List<BasketAlertTriggeredEvent>();
        foreach (var basket in alerts.GroupBy(a => (a.BasketId, a.UserId)))
        {
            var totals = await BasketTotalsReader.ComputeAsync(dbContext, basket.Key.BasketId, basket.Key.UserId, cancellationToken);

            foreach (var alert in basket)
            {
                var watched = alert.Check(totals);
                if (watched is { } w && alert.HasCurrencyMismatch(w.Currency))
                {
                    // Debug, not Warning: dormancy holds until the currencies agree, so it repeats on every scrape.
                    logger.LogDebug("Basket alert {BasketAlertId} is dormant: target in {AlertCurrency}, total in {TotalCurrency}",
                        alert.Id, alert.Currency, w.Currency);
                    continue;
                }
                if (!alert.ShouldTrigger(watched)) continue;

                logger.LogInformation("Basket alert {BasketAlertId} triggered for basket {BasketId}", alert.Id, alert.BasketId);
                alert.ClaimFiring(now);
                triggered.Add(new BasketAlertTriggeredEvent(
                    alert.Id, alert.BasketId, alert.UserId, watched!.Value.Host, watched.Value.Total, alert.TargetTotal, alert.Currency));
            }
        }

        // The claim, as in CheckAlertsHandler: the xmin token on BasketAlert (Postgres) makes a
        // concurrent worker that stamped the same alerts first win, and this batch emits nothing.
        if (triggered.Count > 0)
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogInformation(ex,
                    "Concurrent worker already claimed basket alerts for product {ProductId}; skipping this batch", @event.ProductId);
                return [];
            }
        }

        return triggered;
    }
}
