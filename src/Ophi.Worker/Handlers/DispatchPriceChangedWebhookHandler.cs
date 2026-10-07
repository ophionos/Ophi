using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Webhooks;
using Wolverine.Attributes;

namespace Ophi.Worker.Handlers;

[WolverineHandler]
[LocalQueue("events")]
public static class DispatchPriceChangedWebhookHandler
{
    public static async Task HandleAsync(
        PriceUpdatedEvent @event,
        OphiDbContext dbContext,
        IWebhookDispatchService webhookDispatchService,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Only dispatch when price actually changed (not same-value re-check)
        if (@event.OldPrice.HasValue && @event.OldPrice.Value == @event.NewPrice)
            return;

        var product = await dbContext.Products
            .Where(p => p.Id == @event.ProductId)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.UserId,
                PrimaryUrl = p.ProductUrls.OrderBy(u => u.CreatedAt).Select(u => u.Url).FirstOrDefault() ?? ""
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (product == null) return;

        var productUrl = product.PrimaryUrl;

        var payload = new WebhookPayload(
            ProductId: product.Id,
            ProductName: product.Name,
            ProductUrl: productUrl,
            OldPrice: @event.OldPrice,
            NewPrice: @event.NewPrice,
            Currency: @event.Currency,
            Timestamp: timeProvider.GetUtcNow().UtcDateTime
        );

        try
        {
            await webhookDispatchService.DispatchAsync(WebhookEvents.PriceChanged, product.UserId, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to dispatch price_changed webhook for product {ProductId}", product.Id);
        }
    }
}
