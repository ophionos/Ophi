namespace Ophi.Infrastructure.Webhooks;

public static class WebhookEvents
{
    public const string AlertFired = "alert_fired";
    public const string PriceChanged = "price_changed";
    public const string ScrapeFailed = "scrape_failed";
    public const string BasketAlertFired = "basket_alert_fired";
}

public record WebhookPayload(
    Guid ProductId,
    string ProductName,
    string ProductUrl,
    decimal? OldPrice,
    decimal? NewPrice,
    string Currency,
    DateTime Timestamp
);

/// <summary>The <c>basket_alert_fired</c> payload: a basket's total at one store reached the target.</summary>
public record BasketAlertWebhookPayload(
    Guid BasketId,
    string BasketName,
    string Store,
    decimal Total,
    decimal TargetTotal,
    string Currency,
    string Url,
    DateTime Timestamp
);

public interface IWebhookDispatchService
{
    /// <summary>Dispatch to all matching, enabled targets for a user.</summary>
    Task DispatchAsync(string eventType, Guid userId, WebhookPayload payload, CancellationToken ct = default);

    /// <summary>Dispatch <c>basket_alert_fired</c> to the user's enabled targets that subscribed to it.</summary>
    Task DispatchBasketAlertAsync(Guid userId, BasketAlertWebhookPayload payload, CancellationToken ct = default);

    /// <summary>Send a synthetic test payload directly to a URL. Returns (success, errorMessage).</summary>
    Task<(bool Success, string? Error)> SendTestAsync(string url, CancellationToken ct = default);
}
