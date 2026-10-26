using Ophi.Domain.Entities;

namespace Ophi.Domain.Services;

/// <summary>
/// The one owner of the shipping rule: price + shipping for one site. Derived at read time and never
/// stored, so stored prices, the aggregator, alerts and history all stay on the scraped price.
/// </summary>
public static class LandedPrice
{
    /// <summary>
    /// The shipping cost for an order of <paramref name="amount"/> on the rate's site: zero with no
    /// rate, zero when <see cref="ShippingRate.FreeAbove"/> is reached, otherwise the flat amount.
    /// Null when the rate is in another currency: exchange rates are display-only, so the cost is
    /// unknown rather than converted. <paramref name="amount"/> is one price or a basket subtotal.
    /// </summary>
    public static decimal? Shipping(decimal amount, string currency, ShippingRate? rate)
    {
        if (rate is null) return 0m;
        if (!string.Equals(rate.Currency, currency, StringComparison.OrdinalIgnoreCase)) return null;
        if (rate.FreeAbove is { } threshold && amount >= threshold) return 0m;
        return rate.Amount;
    }

    /// <summary>Price + <see cref="Shipping"/>, or null when the shipping is unknown.</summary>
    public static decimal? Of(decimal price, string currency, ShippingRate? rate) =>
        Shipping(price, currency, rate) is { } shipping ? price + shipping : null;
}
