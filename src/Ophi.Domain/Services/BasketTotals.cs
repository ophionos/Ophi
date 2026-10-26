using Ophi.Domain.Entities;
using Ophi.Domain.Enums;

namespace Ophi.Domain.Services;

/// <summary>
/// The one owner of basket totals (#135): what the basket costs at each store, which store is the
/// best, and the cheapest split across stores. A store is a site (<see cref="SiteHost"/>), the same
/// key as <see cref="ShippingRate"/>. Derived per read; nothing is stored.
/// <para>
/// No currency conversion: a store whose item prices are in different currencies has no total, and
/// the best store and the split stay inside one currency (<see cref="ProductPriceAggregator.DominantCurrency"/>).
/// </para>
/// </summary>
public static class BasketTotals
{
    /// <summary>One basket item: the product's URLs must be loaded.</summary>
    public sealed record Item(Guid ProductId, int Quantity, IReadOnlyList<ProductUrl> Urls);

    /// <summary>The unit price of one item at a store; null when the store does not sell it.</summary>
    public sealed record StoreItem(Guid ProductId, decimal? Price, string? Currency);

    /// <summary>
    /// The basket at one store. <see cref="Currency"/>, <see cref="Subtotal"/> and <see cref="Total"/>
    /// are null when its prices are in different currencies; <see cref="Shipping"/> and
    /// <see cref="Total"/> are null when its shipping rate is in another currency. An incomplete store
    /// carries the subtotal of the items it has.
    /// </summary>
    public sealed record StoreTotal(
        string Host, string? Currency, IReadOnlyList<StoreItem> Items,
        decimal? Subtotal, decimal? Shipping, decimal? Total, bool Complete, int MissingCount);

    /// <summary>Where the split buys one item; host and price are null when no URL is in the split's currency.</summary>
    public sealed record SplitLine(Guid ProductId, string? Host, decimal? Price);

    public sealed record HostShipping(string Host, decimal? Shipping);

    /// <summary>
    /// Each item at its cheapest URL in <see cref="Currency"/>, plus shipping once for each host used
    /// (a threshold applies to that host's part). <see cref="Total"/> covers the bought lines and is
    /// null when a shipping cost is unknown.
    /// </summary>
    public sealed record Split(string Currency, IReadOnlyList<SplitLine> Lines, IReadOnlyList<HostShipping> Shipping,
        decimal? Total, bool Complete);

    public sealed record Result(IReadOnlyList<StoreTotal> Stores, string? BestStoreHost, Split? Split);

    private sealed record Offer(Guid ProductId, string Host, decimal Price, string Currency);

    public static Result Compute(IReadOnlyList<Item> items, Func<string, ShippingRate?> rateFor)
    {
        if (items.Count == 0) return new Result([], null, null);

        // Live offers only: the URLs that could be bought now. Paused, out-of-stock and unpriced
        // URLs take no part, nor does a URL whose host cannot be read.
        var offers = items
            .SelectMany(item => item.Urls
                .Where(u => u.Status != ProductUrlStatus.Paused && !u.IsOutOfStock && u.CurrentPrice != null)
                .Select(u => (Url: u, Host: SiteHost.Of(u.Url)))
                .Where(x => x.Host != null)
                .Select(x => new Offer(item.ProductId, x.Host!, x.Url.CurrentPrice!.Value, x.Url.Currency.ToUpperInvariant())))
            .ToList();
        var quantities = items.ToDictionary(i => i.ProductId, i => i.Quantity);

        var stores = offers
            .GroupBy(o => o.Host, StringComparer.Ordinal)
            .Select(g => StoreFor(g.Key, g.ToList(), items, quantities, rateFor))
            .OrderBy(s => s.Complete ? 0 : 1)
            .ThenBy(s => s.Total ?? decimal.MaxValue)
            .ThenBy(s => s.MissingCount)
            .ThenBy(s => s.Host, StringComparer.Ordinal)
            .ToList();

        // The best store: the cheapest complete total, inside the dominant currency of those totals.
        var ranked = stores.Where(s => s is { Complete: true, Total: not null }).ToList();
        var dominant = ProductPriceAggregator.DominantCurrency(ranked.Select(s => s.Currency!));
        var best = ranked
            .Where(s => s.Currency == dominant)
            .OrderBy(s => s.Total)
            .ThenBy(s => s.Host, StringComparer.Ordinal)
            .FirstOrDefault();

        return new Result(stores, best?.Host, SplitFor(items, offers, quantities, rateFor));
    }

    private static StoreTotal StoreFor(string host, List<Offer> offers, IReadOnlyList<Item> items,
        Dictionary<Guid, int> quantities, Func<string, ShippingRate?> rateFor)
    {
        var storeItems = items
            .Select(item => offers
                .Where(o => o.ProductId == item.ProductId)
                .OrderBy(o => o.Price)
                .ThenBy(o => o.Currency, StringComparer.Ordinal)
                .Select(o => new StoreItem(item.ProductId, o.Price, o.Currency))
                .FirstOrDefault() ?? new StoreItem(item.ProductId, null, null))
            .ToList();
        var missing = storeItems.Count(i => i.Price == null);

        // Cheapest per item first, then the currency check: one item listed in two currencies at
        // one host counts in the currency of its cheaper listing.
        var currencies = offers
            .Where(o => storeItems.Any(i => i.ProductId == o.ProductId && i.Price == o.Price && i.Currency == o.Currency))
            .Select(o => o.Currency)
            .Distinct()
            .ToList();
        if (currencies.Count != 1)
            return new StoreTotal(host, null, storeItems, null, null, null, missing == 0, missing);

        var currency = currencies[0];
        var subtotal = storeItems.Where(i => i.Price != null).Sum(i => i.Price!.Value * quantities[i.ProductId]);
        var shipping = LandedPrice.Shipping(subtotal, currency, rateFor(host));
        return new StoreTotal(host, currency, storeItems, subtotal, shipping, subtotal + shipping, missing == 0, missing);
    }

    private static Split? SplitFor(IReadOnlyList<Item> items, List<Offer> offers,
        Dictionary<Guid, int> quantities, Func<string, ShippingRate?> rateFor)
    {
        // One vote per item and currency, so an item listed at many sites does not outvote the rest.
        var currency = ProductPriceAggregator.DominantCurrency(
            offers.Select(o => (o.ProductId, o.Currency)).Distinct().Select(x => x.Currency));
        if (currency == null) return null;

        var lines = items
            .Select(item => offers
                .Where(o => o.ProductId == item.ProductId && o.Currency == currency)
                .OrderBy(o => o.Price)
                .ThenBy(o => o.Host, StringComparer.Ordinal)
                .Select(o => new SplitLine(item.ProductId, o.Host, o.Price))
                .FirstOrDefault() ?? new SplitLine(item.ProductId, null, null))
            .ToList();

        var shipping = lines
            .Where(l => l.Host != null)
            .GroupBy(l => l.Host!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new HostShipping(g.Key,
                LandedPrice.Shipping(g.Sum(l => l.Price!.Value * quantities[l.ProductId]), currency, rateFor(g.Key))))
            .ToList();

        var bought = lines.Where(l => l.Price != null).Sum(l => l.Price!.Value * quantities[l.ProductId]);
        decimal? total = shipping.Any(s => s.Shipping == null) ? null : bought + shipping.Sum(s => s.Shipping!.Value);
        return new Split(currency, lines, shipping, total, lines.All(l => l.Host != null));
    }
}
