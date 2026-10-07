namespace Ophi.Domain.Messages.Events;

public record AlertTriggeredEvent(
    Guid AlertId,
    Guid ProductId,
    Guid UserId,
    decimal CurrentPrice,
    decimal TargetPrice,
    string Currency);
