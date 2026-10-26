using Ophi.Domain.Entities;

namespace Ophi.Domain.Services;

/// <summary>
/// The basket total per store over time (#150). At each instant where some URL's price changes, each
/// URL holds its last price, and <see cref="BasketTotals"/> prices the basket from those held prices —
/// so the store, completeness, shipping and currency rules are the live ones. Shipping uses the
/// current rate: rates are not versioned. Out-of-stock periods are not in the price history, so a
/// URL that is still scraped holds its price through them.
/// </summary>
public static class BasketHistory
{
    public readonly record struct Price(DateTime At, decimal Amount, string Currency);

    /// <summary>
    /// One URL's prices in time order: the price held at the window start, then its changes.
    /// <paramref name="ReachesNow"/> is false for a URL that is no longer scraped (paused or out of
    /// stock); it then holds its last price only up to that point, the rule of the product history.
    /// </summary>
    public sealed record UrlHistory(string Url, IReadOnlyList<Price> Points, bool ReachesNow);

    public sealed record Item(Guid ProductId, int Quantity, IReadOnlyList<UrlHistory> Urls);

    public readonly record struct Point(DateTime At, decimal Total);

    public sealed record StoreSeries(string Host, string Currency, IReadOnlyList<Point> Points);

    /// <summary>
    /// One series per host in <paramref name="hosts"/>, in that order. A series has a point at each
    /// instant where the store is complete and has a total, in the currency it has at the end; a
    /// point is left out when the total did not change, except the last one, at <paramref name="now"/>.
    /// </summary>
    public static IReadOnlyList<StoreSeries> Compute(IReadOnlyList<Item> items, IReadOnlyList<string> hosts,
        Func<string, ShippingRate?> rateFor, DateTime now)
    {
        var urls = items.SelectMany(i => i.Urls).ToList();
        var instants = urls
            .SelectMany(u => u.Points.Select(p => p.At)
                .Concat(!u.ReachesNow && u.Points.Count > 0 ? [u.Points[^1].At.AddTicks(1)] : []))
            .Append(now)
            .Where(at => at <= now)
            .Distinct()
            .Order()
            .ToList();

        var totals = hosts.ToDictionary(h => h, _ => new List<(DateTime At, decimal Total, string Currency)>(), StringComparer.Ordinal);
        foreach (var at in instants)
        {
            var snapshot = items
                .Select(item => new BasketTotals.Item(item.ProductId, item.Quantity,
                    item.Urls.Select(u => HeldAt(u, at) is { } held
                        ? new ProductUrl { Url = u.Url, CurrentPrice = held.Amount, Currency = held.Currency }
                        : new ProductUrl { Url = u.Url, CurrentPrice = null }).ToList()))
                .ToList();

            foreach (var store in BasketTotals.Compute(snapshot, rateFor).Stores)
            {
                if (store is { Complete: true, Total: { } total, Currency: { } currency } &&
                    totals.TryGetValue(store.Host, out var series))
                {
                    series.Add((at, total, currency));
                }
            }
        }

        return hosts
            .Where(h => totals[h].Count > 0)
            .Select(h =>
            {
                var currency = totals[h][^1].Currency;
                var inCurrency = totals[h].Where(t => t.Currency == currency).ToList();
                var points = inCurrency
                    .Where((t, i) => i == 0 || i == inCurrency.Count - 1 || t.Total != inCurrency[i - 1].Total)
                    .Select(t => new Point(t.At, t.Total))
                    .ToList();
                return new StoreSeries(h, currency, points);
            })
            .ToList();
    }

    private static Price? HeldAt(UrlHistory url, DateTime at)
    {
        Price? held = null;
        foreach (var point in url.Points)
        {
            if (point.At > at) break;
            held = point;
        }

        if (held is null || (!url.ReachesNow && at > url.Points[^1].At)) return null;
        return held;
    }
}
