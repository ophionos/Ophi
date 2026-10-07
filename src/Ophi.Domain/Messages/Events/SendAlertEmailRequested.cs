using Ophi.Domain.Enums;

namespace Ophi.Domain.Messages.Events;

/// <summary>
/// Cascaded by <c>SendAlertNotificationHandler</c> after the in-app notification is persisted.
/// Runs on the notifications queue so failures retry independently of Discord and webhook channels.
/// </summary>
/// <param name="Condition">
/// Needed to render <paramref name="TargetPrice"/>, which is an amount for Below/Above but a
/// percentage for PercentDrop. Declared last with a null default so messages already sitting in the
/// durable queue at deploy time still deserialize; null means "unknown", and
/// <c>AlertTargetFormatter</c> falls back to the amount rendering for it.
/// </param>
public record SendAlertEmailRequested(
    Guid AlertId,
    string RecipientEmail,
    string RecipientName,
    string ProductName,
    string ProductUrl,
    decimal CurrentPrice,
    decimal TargetPrice,
    string Currency,
    AlertCondition? Condition = null);
