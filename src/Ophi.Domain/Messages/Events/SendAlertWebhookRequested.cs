namespace Ophi.Domain.Messages.Events;

/// <summary>
/// Cascaded by <c>SendAlertNotificationHandler</c> so user-configured outbound webhooks fire
/// independently of email and Discord. Retried by Wolverine if a downstream webhook fails.
/// </summary>
public record SendAlertWebhookRequested(
    Guid AlertId,
    Guid UserId,
    Guid ProductId,
    string ProductName,
    string ProductUrl,
    decimal? OldPrice,
    decimal CurrentPrice,
    string Currency);
