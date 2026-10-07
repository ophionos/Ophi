using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Webhooks;
using Wolverine.Attributes;

namespace Ophi.Worker.Handlers;

/// <summary>
/// Channel handler for the outbound-webhook leg of an alert fire. Dispatches the
/// user-configured webhook targets for the AlertFired event; failures retry inside
/// Wolverine without affecting other channels.
/// </summary>
[WolverineHandler]
[LocalQueue("notifications")]
public static class SendAlertWebhookHandler
{
    public static async Task HandleAsync(
        SendAlertWebhookRequested @event,
        IWebhookDispatchService webhookDispatchService,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var payload = new WebhookPayload(
            ProductId: @event.ProductId,
            ProductName: @event.ProductName,
            ProductUrl: @event.ProductUrl,
            OldPrice: @event.OldPrice,
            NewPrice: @event.CurrentPrice,
            Currency: @event.Currency,
            Timestamp: timeProvider.GetUtcNow().UtcDateTime);

        await webhookDispatchService.DispatchAsync(WebhookEvents.AlertFired, @event.UserId, payload, cancellationToken);

        logger.LogInformation("Alert webhooks dispatched for product {ProductName} (alert {AlertId})",
            @event.ProductName, @event.AlertId);
    }
}
