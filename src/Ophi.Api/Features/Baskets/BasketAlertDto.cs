using Ophi.Domain.Entities;
using Ophi.Domain.Services;

namespace Ophi.Api.Features.Baskets;

/// <param name="Host">The watched site; null watches the best complete store.</param>
/// <param name="WatchedHost">The store watched now, with its total; null when it has no total to compare.</param>
/// <param name="HasCurrencyMismatch">True when the watched total is in another currency than the target: the alert is dormant.</param>
public record BasketAlertDto(
    Guid Id,
    Guid BasketId,
    string? Host,
    decimal TargetTotal,
    string Currency,
    bool Active,
    DateTime? LastTriggered,
    int TriggerCount,
    DateTime CreatedAt,
    string? WatchedHost,
    decimal? WatchedTotal,
    string? WatchedCurrency,
    bool HasCurrencyMismatch)
{
    public static BasketAlertDto From(BasketAlert alert, BasketTotals.Result totals)
    {
        var watched = alert.Check(totals);
        return new BasketAlertDto(alert.Id, alert.BasketId, alert.Host, alert.TargetTotal, alert.Currency, alert.IsActive,
            alert.LastTriggeredAt, alert.TriggerCount, alert.CreatedAt,
            watched?.Host, watched?.Total, watched?.Currency,
            watched is { } w && alert.HasCurrencyMismatch(w.Currency));
    }
}

public record BasketAlertListDto(List<BasketAlertDto> Items);

/// <summary>
/// Basket alerts have their own active limit: <c>Alerts:MaxAlertsPerUser</c> counted over basket alerts
/// only. Product alerts keep their own count. Create, resume and restore enforce it.
/// </summary>
internal static class BasketAlertLimit
{
    public static Common.Exceptions.ApiException Reached(int max) =>
        new($"Maximum number of active basket alerts ({max}) reached.", 422, "MaxAlertsReached");
}
