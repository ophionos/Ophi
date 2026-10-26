namespace Ophi.Domain.Messages.Events;

/// <summary>A basket alert (#151) claimed a firing: <paramref name="Total"/> is the watched store's total.</summary>
public record BasketAlertTriggeredEvent(
    Guid BasketAlertId,
    Guid BasketId,
    Guid UserId,
    string Host,
    decimal Total,
    decimal TargetTotal,
    string Currency);
