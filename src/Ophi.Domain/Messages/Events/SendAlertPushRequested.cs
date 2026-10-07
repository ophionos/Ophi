using Ophi.Domain.Enums;

namespace Ophi.Domain.Messages.Events;

/// <summary>
/// Cascaded by <c>SendAlertNotificationHandler</c> when the user has Telegram enabled and a chat id
/// set. Same contract as <see cref="SendAlertDiscordRequested"/>: the orchestrator does the
/// enabled-check, the channel handler sends unconditionally and rethrows so Wolverine retries.
/// </summary>
public record SendAlertTelegramRequested(
    Guid AlertId,
    string ChatId,
    string ProductName,
    string ProductUrl,
    decimal CurrentPrice,
    decimal TargetPrice,
    string Currency,
    AlertCondition? Condition = null);

/// <summary>Pushover counterpart of <see cref="SendAlertTelegramRequested"/>.</summary>
public record SendAlertPushoverRequested(
    Guid AlertId,
    string UserKey,
    string ProductName,
    string ProductUrl,
    decimal CurrentPrice,
    decimal TargetPrice,
    string Currency,
    AlertCondition? Condition = null);
