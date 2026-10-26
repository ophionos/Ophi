using Ophi.Domain.Services;

namespace Ophi.Domain.Entities;

/// <summary>
/// An alert on a basket total (#151): fires when the watched total is at or under
/// <see cref="TargetTotal"/>, the rule of <see cref="Enums.AlertCondition.Below"/>. <see cref="Host"/>
/// set watches that store (site); null watches the best complete store. Only a complete store has a
/// total to watch, so an incomplete store never fires. Totals come from <see cref="BasketTotals"/>.
/// <para>
/// Denominated on purpose, like <see cref="Alert"/>: when the watched total is in another currency
/// than <see cref="Currency"/>, the alert is dormant. No conversion.
/// </para>
/// </summary>
public class BasketAlert : BaseEntity
{
    // State changes only through the methods below; the init accessors serve construction (and test
    // arrange) only. EF Core maps each property through its _camelCase backing field by convention.
    private bool _isActive = true;
    private DateTime? _lastTriggeredAt;
    private int _triggerCount;

    public decimal TargetTotal { get; init; }
    public string Currency { get; init; } = "USD";

    /// <summary>The watched site, normalized (<see cref="SiteHost"/>); null for the best store.</summary>
    public string? Host { get; init; }

    public bool IsActive { get => _isActive; init => _isActive = value; }
    public DateTime? LastTriggeredAt { get => _lastTriggeredAt; init => _lastTriggeredAt = value; }
    public int TriggerCount { get => _triggerCount; init => _triggerCount = value; }

    // Foreign keys
    public Guid BasketId { get; init; }
    public Guid UserId { get; init; }

    // Navigation properties
    public Basket Basket { get; init; } = null!;
    public User User { get; init; } = null!;

    /// <summary>The total the alert watches: a complete store with a total.</summary>
    public readonly record struct Watched(string Host, decimal Total, string Currency);

    /// <summary>
    /// The watched store in <paramref name="totals"/>, or null when it has no total to compare: it is
    /// incomplete, mixes currencies, has shipping in another currency, or (best store) there is none.
    /// </summary>
    public Watched? Check(BasketTotals.Result totals)
    {
        var host = Host is null ? totals.BestStoreHost : SiteHost.Normalize(Host);
        if (host is null) return null;

        var store = totals.Stores.FirstOrDefault(s => s.Host == host);
        return store is { Complete: true, Total: { } total, Currency: { } currency }
            ? new Watched(store.Host, total, currency)
            : null;
    }

    /// <summary>True when the alert is active, the watched total is in <see cref="Currency"/>, and it is at or under the target.</summary>
    public bool ShouldTrigger(Watched? watched) =>
        IsActive && watched is { } w && !HasCurrencyMismatch(w.Currency) && w.Total <= TargetTotal;

    public bool HasCurrencyMismatch(string totalCurrency) =>
        !string.Equals(Currency, totalCurrency, StringComparison.OrdinalIgnoreCase);

    /// <summary>See <see cref="Alert.ClaimFiring"/>: the checker's claim, saved under the xmin token. Caller persists.</summary>
    public void ClaimFiring(DateTime now) => _lastTriggeredAt = now;

    /// <summary>See <see cref="Alert.Trigger"/>: called once the notification commits. Caller persists.</summary>
    public void Trigger(DateTime now)
    {
        _lastTriggeredAt = now;
        _triggerCount++;
    }

    public void Pause() => _isActive = false;

    public void Resume() => _isActive = true;

    public bool IsInCooldown(TimeSpan cooldown, DateTime now) =>
        LastTriggeredAt.HasValue && now - LastTriggeredAt.Value < cooldown;
}
