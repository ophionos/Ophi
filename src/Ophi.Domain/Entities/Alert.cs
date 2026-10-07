using Ophi.Domain.Enums;

namespace Ophi.Domain.Entities;

public class Alert : BaseEntity
{
    /// <summary>
    /// Settable only so <see cref="Redenominate"/> can replace it — the target is otherwise fixed
    /// for the life of the alert. Assign it directly at construction and nowhere else.
    /// </summary>
    public decimal TargetPrice { get; set; }

    public decimal ReferencePrice { get; init; }

    /// <summary>
    /// Denomination of <see cref="TargetPrice"/>, stamped from the product's currency when the
    /// alert is created. A price is a number *and* a denomination: without this, a USD 80 target
    /// silently starts being compared against EUR prices the moment a product re-anchors to
    /// another currency (see <see cref="Services.ProductPriceAggregator"/>), and fires on a
    /// number that never meant what the user asked for.
    /// Carries no meaning for <see cref="AlertCondition.PercentDrop"/>, whose target is a percentage.
    /// Settable only so <see cref="Redenominate"/> can replace it; see the note on
    /// <see cref="TargetPrice"/>.
    /// </summary>
    public string Currency { get; set; } = "USD";

    public AlertCondition Condition { get; init; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastTriggeredAt { get; set; }
    public int TriggerCount { get; set; }

    // Foreign keys
    public Guid ProductId { get; init; }
    public Guid UserId { get; init; }

    // Navigation properties
    public Product Product { get; init; } = null!;
    public User User { get; init; } = null!;

    /// <summary>
    /// Records that the alert has fired at <paramref name="now"/>: stamps <see cref="LastTriggeredAt"/>
    /// and increments <see cref="TriggerCount"/>. Caller persists via the DbContext.
    /// </summary>
    public void Trigger(DateTime now)
    {
        LastTriggeredAt = now;
        TriggerCount++;
    }

    /// <summary>
    /// Stops the alert firing without deleting it. Trigger history is kept. Caller persists.
    /// </summary>
    public void Pause() => IsActive = false;

    /// <summary>
    /// Lets a paused alert fire again. The per-user active-alert cap is the caller's to enforce —
    /// it needs a count this entity cannot see. Caller persists.
    /// </summary>
    public void Resume() => IsActive = true;

    /// <summary>
    /// Returns true when the alert last fired within the cooldown window and should not re-fire.
    /// Alerts that have never fired are never in cooldown.
    /// </summary>
    public bool IsInCooldown(TimeSpan cooldown, DateTime now) =>
        LastTriggeredAt.HasValue && now - LastTriggeredAt.Value < cooldown;

    /// <summary>
    /// True when this alert's <see cref="TargetPrice"/> can no longer be meaningfully compared
    /// against the product, because the product is now denominated in a different currency.
    /// Such an alert is dormant: it will not fire until the currencies agree again.
    /// Always false for <see cref="AlertCondition.PercentDrop"/>, whose target is a percentage and
    /// therefore survives a re-denomination unchanged.
    /// </summary>
    public bool HasCurrencyMismatch(string productCurrency) =>
        IsCurrencyMismatch(Currency, Condition, productCurrency);

    /// <summary>
    /// Moves a dormant alert onto <paramref name="productCurrency"/> with a new target, ending its
    /// dormancy. This is the only way an alert's denomination changes after creation.
    /// <para>
    /// <paramref name="newTargetPrice"/> is required rather than optional because the existing
    /// target cannot be carried over: it is an amount in the old currency, and reusing the number
    /// under a new denomination is exactly the silent re-denomination
    /// <see cref="HasCurrencyMismatch"/> exists to catch. Converting it automatically is not an
    /// option either — that would need an FX rate, and a target is a decision the user made, not a
    /// quantity to be translated.
    /// </para>
    /// Trigger history is left intact: it records what already happened and is not state about the
    /// current target.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the alert is not dormant against <paramref name="productCurrency"/> — either
    /// because it already uses that currency, or because it is a
    /// <see cref="AlertCondition.PercentDrop"/>, which never goes dormant. Changing a live alert's
    /// target is a different operation than recovering a dormant one.
    /// </exception>
    public void Redenominate(decimal newTargetPrice, string productCurrency)
    {
        if (!HasCurrencyMismatch(productCurrency))
        {
            throw new InvalidOperationException(
                $"Alert is not dormant against {productCurrency}; there is nothing to re-denominate.");
        }

        TargetPrice = newTargetPrice;
        Currency = productCurrency;
    }

    /// <summary>
    /// The dormancy rule itself, callable without an <see cref="Alert"/> instance so read paths that
    /// project columns straight out of EF can apply it without materializing entities — one
    /// definition of "dormant" rather than a re-implementation per query.
    /// </summary>
    public static bool IsCurrencyMismatch(
        string alertCurrency, AlertCondition condition, string productCurrency) =>
        condition != AlertCondition.PercentDrop &&
        !string.Equals(alertCurrency, productCurrency, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Evaluates whether the alert should fire.
    /// <para>
    /// <paramref name="currentCurrency"/> is required rather than optional on purpose: every
    /// comparison here is between raw decimals, which is only valid within one denomination. An
    /// optional currency would let a call site silently opt out of the check and reintroduce the
    /// bug this guards, so the compiler forces each caller to say what the number is denominated in.
    /// </para>
    /// Returns false when the alert is inactive, when the currencies are incomparable, or when the
    /// percent-drop reference price is missing/zero.
    /// </summary>
    /// <param name="currentPrice">The product's current headline price.</param>
    /// <param name="currentCurrency">Denomination of <paramref name="currentPrice"/>.</param>
    /// <param name="oldPrice">Prior price, used only for <see cref="AlertCondition.PercentDrop"/>.</param>
    /// <param name="oldCurrency">
    /// Denomination of <paramref name="oldPrice"/>. Null means "unknown" — see
    /// <see cref="EvaluatePercentDrop"/> for why that is treated as unchanged rather than as a mismatch.
    /// </param>
    public bool ShouldTrigger(
        decimal currentPrice,
        string currentCurrency,
        decimal? oldPrice = null,
        string? oldCurrency = null)
    {
        if (!IsActive) return false;

        // Below/Above compare an amount against an amount, so they need the denominations to agree.
        // PercentDrop is exempt: its target is a percentage, meaningful in any currency.
        if (HasCurrencyMismatch(currentCurrency)) return false;

        return Condition switch
        {
            AlertCondition.Below => currentPrice <= TargetPrice,
            AlertCondition.Above => currentPrice >= TargetPrice,
            AlertCondition.PercentDrop => EvaluatePercentDrop(oldPrice, oldCurrency, currentPrice, currentCurrency),
            _ => false
        };
    }

    private bool EvaluatePercentDrop(
        decimal? oldPrice, string? oldCurrency, decimal currentPrice, string currentCurrency)
    {
        if (oldPrice is null or 0)
            return false;

        // (old - new) / old is only a percentage if both sides are the same denomination. A product
        // that re-anchored mid-scrape (USD 100 -> EUR 75) would otherwise read as a 25% drop that
        // never happened. A null oldCurrency means the event predates the field — in-flight durable
        // messages during a deploy only — and the alert-vs-product guard above already blocks the
        // persistent-flip case, so assume it held rather than suppress a genuine drop.
        if (oldCurrency is not null &&
            !string.Equals(oldCurrency, currentCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var percentChange = (oldPrice.Value - currentPrice) / oldPrice.Value * 100;
        return percentChange >= TargetPrice;
    }
}
