using Ophi.Domain.Entities;
using Ophi.Domain.Enums;

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

    /// <summary>One URL's site, shipping and landed price. Shipping and landed are null when the URL
    /// has no price or its site's shipping is in another currency.</summary>
    public readonly record struct UrlLanded(string? Host, decimal? Shipping, decimal? Price);

    /// <summary>A product's cheapest landed price, the shipping inside it, and the site that gives it.</summary>
    public readonly record struct ProductLanded(decimal Price, decimal Shipping, string Host);

    public static UrlLanded ForUrl(ProductUrl url, Func<string, ShippingRate?> rateFor)
    {
        var host = SiteHost.Of(url.Url);
        if (url.CurrentPrice is not { } price) return new UrlLanded(host, null, null);

        var shipping = Shipping(price, url.Currency, host is null ? null : rateFor(host));
        return new UrlLanded(host, shipping, shipping is { } s ? price + s : null);
    }

    /// <summary>
    /// The MIN landed price over the URLs <see cref="ProductPriceAggregator"/> lets set the headline
    /// price: not paused, priced, and in the product's currency. With shipping, a dearer item at a
    /// site with free shipping can win. URLs whose shipping is unknown take no part; null when no
    /// URL is left. Equal landed prices break on the host, so every caller names the same site.
    /// Requires <see cref="Product.ProductUrls"/> to be loaded.
    /// </summary>
    public static ProductLanded? ForProduct(Product product, Func<string, ShippingRate?> rateFor) =>
        product.ProductUrls
            .Where(u => u.Status != ProductUrlStatus.Paused &&
                        string.Equals(u.Currency, product.Currency, StringComparison.OrdinalIgnoreCase))
            .Select(u => ForUrl(u, rateFor))
            .Where(l => l is { Host: not null, Price: not null })
            .Select(l => new ProductLanded(l.Price!.Value, l.Shipping!.Value, l.Host!))
            .OrderBy(l => l.Price)
            .ThenBy(l => l.Host, StringComparer.Ordinal)
            .Cast<ProductLanded?>()
            .FirstOrDefault();
}
