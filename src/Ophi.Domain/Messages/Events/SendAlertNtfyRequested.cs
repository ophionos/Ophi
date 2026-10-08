using Ophi.Domain.Enums;

namespace Ophi.Domain.Messages.Events;

/// <summary>ntfy counterpart of <see cref="SendAlertTelegramRequested"/>.</summary>
public record SendAlertNtfyRequested(
    Guid AlertId,
    string TopicUrl,
    string ProductName,
    string ProductUrl,
    decimal CurrentPrice,
    decimal TargetPrice,
    string Currency,
    AlertCondition? Condition = null);
