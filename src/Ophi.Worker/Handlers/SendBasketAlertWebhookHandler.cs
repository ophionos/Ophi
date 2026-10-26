using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Webhooks;
using Wolverine.Attributes;

namespace Ophi.Worker.Handlers;

/// <summary>
/// Channel handler for the outbound-webhook leg of a basket alert fire (<c>basket_alert_fired</c>).
/// Failures retry inside Wolverine without affecting the other channels.
/// </summary>
[WolverineHandler]
[LocalQueue("notifications")]
public static class SendBasketAlertWebhookHandler
{
    public static async Task HandleAsync(
        SendBasketAlertWebhookRequested @event,
        IWebhookDispatchService webhookDispatchService,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var payload = new BasketAlertWebhookPayload(
            @event.BasketId, @event.BasketName, @event.Store, @event.Total, @event.TargetTotal,
            @event.Currency, @event.Url, timeProvider.GetUtcNow().UtcDateTime);

        await webhookDispatchService.DispatchBasketAlertAsync(@event.UserId, payload, cancellationToken);

        logger.LogInformation("Basket alert webhooks dispatched for basket {BasketId} (alert {AlertId})",
            @event.BasketId, @event.BasketAlertId);
    }
}
