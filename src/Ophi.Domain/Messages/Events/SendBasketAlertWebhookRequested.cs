namespace Ophi.Domain.Messages.Events;

/// <summary>
/// Cascaded by <c>SendBasketAlertNotificationHandler</c> so the user's outbound webhooks subscribed to
/// <c>basket_alert_fired</c> fire independently of the other channels. Its own event and payload, not
/// <c>alert_fired</c>: that payload is keyed on a product.
/// </summary>
public record SendBasketAlertWebhookRequested(
    Guid BasketAlertId,
    Guid UserId,
    Guid BasketId,
    string BasketName,
    string Store,
    decimal Total,
    decimal TargetTotal,
    string Currency,
    string Url);
