using Ophi.Domain.Enums;

namespace Ophi.Domain.Messages.Events;

/// <summary>
/// Cascaded by <c>SendAlertNotificationHandler</c> when the user has Discord notifications
/// enabled and a webhook URL configured. The orchestrator handles the enabled-check so this
/// handler can fire the webhook unconditionally.
/// </summary>
/// <param name="Condition">
/// Needed to render <paramref name="TargetPrice"/> — an amount for Below/Above, a percentage for
/// PercentDrop. Trailing with a null default for durable-queue compatibility; see
/// <see cref="SendAlertEmailRequested"/>.
/// </param>
public record SendAlertDiscordRequested(
    Guid AlertId,
    string DiscordWebhookUrl,
    string ProductName,
    string ProductUrl,
    decimal CurrentPrice,
    decimal TargetPrice,
    string Currency,
    AlertCondition? Condition = null);
