using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Entities;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Persistence;
using Wolverine.Attributes;

namespace Ophi.Worker.Handlers;

[WolverineHandler]
[LocalQueue("events")]
public static class RecordPriceHistoryHandler
{
    public static async Task HandleAsync(
        PriceUpdatedEvent @event,
        OphiDbContext dbContext,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Recording price history for product {ProductId}", @event.ProductId);

        // History is per-URL — log the URL-level price (what was just scraped at this URL), not the
        // product MIN, which may belong to a different URL. For single-URL events both coincide,
        // so fall back to NewPrice/Currency when the URL-level fields are absent.
        var urlPrice = @event.UrlPrice ?? @event.NewPrice;
        var urlCurrency = @event.UrlCurrency ?? @event.Currency;

        // Skip recording if price hasn't changed from the last recorded point for this URL
        var lastPrice = await dbContext.PricePoints
            .Where(pp => pp.ProductUrlId == @event.ProductUrlId)
            .OrderByDescending(pp => pp.RecordedAt)
            .Select(pp => (decimal?)pp.Price)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastPrice.HasValue && lastPrice.Value == urlPrice)
        {
            logger.LogDebug("Price unchanged for product {ProductId} ({Price}), skipping price point",
                @event.ProductId, urlPrice);
            return;
        }

        var pricePoint = new PricePoint
        {
            Id = Guid.NewGuid(),
            ProductId = @event.ProductId,
            ProductUrlId = @event.ProductUrlId,
            Price = urlPrice,
            Currency = urlCurrency,
            RecordedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        dbContext.PricePoints.Add(pricePoint);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Recorded price point for product {ProductId}: {Price} {Currency}",
            @event.ProductId, urlPrice, urlCurrency);
    }
}
