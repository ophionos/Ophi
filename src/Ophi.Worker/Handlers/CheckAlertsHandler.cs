using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Wolverine.Attributes;

namespace Ophi.Worker.Handlers;

[WolverineHandler]
[LocalQueue("events")]
public static class CheckAlertsHandler
{
    public static async Task<IEnumerable<AlertTriggeredEvent>> HandleAsync(
        PriceUpdatedEvent @event,
        OphiDbContext dbContext,
        IOptions<AlertSettings> alertSettings,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cooldownPeriod = TimeSpan.FromMinutes(alertSettings.Value.CooldownMinutes);

        logger.LogDebug("Checking alerts for product {ProductId}", @event.ProductId);

        var alerts = await dbContext.Alerts
            .Where(a => a.IsActive && a.ProductId == @event.ProductId)
            .Include(a => a.Product)
            .Include(a => a.User)
            .ToListAsync(cancellationToken);

        if (alerts.Count == 0)
        {
            return [];
        }

        var triggeredEvents = new List<AlertTriggeredEvent>();

        foreach (var alert in alerts)
        {
            if (alert.IsInCooldown(cooldownPeriod, now))
                continue;

            // Use product-level CurrentPrice (minimum across all URLs) rather than
            // the URL-level price from the event, so alerts fire on the actual best price
            var currentPrice = alert.Product.CurrentPrice;
            if (currentPrice == null)
                continue;

            // Log the dormant case distinctly rather than hiding it inside a generic "did not
            // trigger" — but at Debug, not Warning. Dormancy is a persistent state, not an event:
            // it holds until the product re-anchors or the alert is recreated, so a Warning here
            // would repeat on every scrape of that product, forever. The durable surface for this
            // is the API's `hasCurrencyMismatch` (GetAlerts/GetProduct), which the product page
            // renders as an explicit "Dormant" banner; this line is for tracing one evaluation.
            if (alert.HasCurrencyMismatch(alert.Product.Currency))
            {
                logger.LogDebug(
                    "Alert {AlertId} is dormant: target is in {AlertCurrency} but product {ProductId} is now priced in {ProductCurrency}",
                    alert.Id, alert.Currency, @event.ProductId, alert.Product.Currency);
                continue;
            }

            if (!alert.ShouldTrigger(
                    currentPrice.Value, alert.Product.Currency, @event.OldPrice, @event.OldCurrency))
                continue;

            logger.LogInformation("Alert {AlertId} triggered for product {ProductId}",
                alert.Id, @event.ProductId);

            // Stamp LastTriggeredAt here (not TriggerCount — that increments in SendAlertNotificationHandler
            // once the in-app notification commits, so a cooldown race doesn't double-count).
            alert.ClaimFiring(now);

            triggeredEvents.Add(new AlertTriggeredEvent(
                alert.Id,
                @event.ProductId,
                alert.UserId,
                currentPrice.Value,
                alert.TargetPrice,
                alert.Product.Currency));
        }

        // Persist LastTriggeredAt before emitting events. This write is the claim on the alert
        // firing: the Alert's xmin concurrency token (Postgres) means that if another worker already
        // stamped these same alerts for a concurrent PriceUpdatedEvent, our SaveChanges loses the race
        // and throws. We then emit nothing — the winner loaded the same eligible alert set and will
        // fire all of them, so dropping the whole batch here double-covers nothing and avoids a
        // duplicate notification. (On SQLite there's no xmin; the serial "events" queue is the guard.)
        if (triggeredEvents.Count > 0)
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogInformation(ex,
                    "Concurrent worker already claimed alerts for product {ProductId}; skipping this batch",
                    @event.ProductId);
                return [];
            }
        }

        return triggeredEvents;
    }
}
