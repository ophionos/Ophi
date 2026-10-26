namespace Ophi.Domain.Services;

/// <summary>
/// The product price over time, rebuilt from change-only price points. Each URL holds its last
/// price until its next point, and the product price at an instant is the MIN of the prices its
/// URLs hold then, in one currency — the rule <see cref="ProductPriceAggregator"/> applies to
/// the live price.
/// </summary>
public static class PriceSeries
{
    public readonly record struct Point(DateTime At, decimal Price, string Currency);

    /// <summary>
    /// One URL's points in time order: the price it held when the window opened, then its changes.
    /// <paramref name="ReachesNow"/> is false for a URL that is no longer scraped (paused, out of
    /// stock, or deleted); its last price then holds only to the end of that point's day, because
    /// nothing says it still held after.
    /// </summary>
    public sealed record UrlSeries(IReadOnlyList<Point> Points, bool ReachesNow);

    /// <summary>
    /// The MIN of the prices the URLs hold at <paramref name="at"/>. A URL whose held price is in
    /// another currency takes no part (exchange rates are display-only). Null when no URL holds a price.
    /// </summary>
    public static decimal? MinAt(IEnumerable<UrlSeries> urls, string currency, DateTime at)
    {
        decimal? min = null;
        foreach (var url in urls)
        {
            if (HeldAt(url, at) is { } held &&
                string.Equals(held.Currency, currency, StringComparison.OrdinalIgnoreCase) &&
                (min is null || held.Price < min))
            {
                min = held.Price;
            }
        }
        return min;
    }

    /// <summary>
    /// One value per UTC day for the last <paramref name="days"/> days, today included: the
    /// product price at the end of the day (at <paramref name="now"/> for today). Days with no
    /// price, such as days before the first point, are left out.
    /// </summary>
    public static List<(DateOnly Day, decimal Price)> Daily(
        IReadOnlyCollection<UrlSeries> urls, string currency, DateTime now, int days)
    {
        var result = new List<(DateOnly, decimal)>(days);
        for (var back = days - 1; back >= 0; back--)
        {
            var day = now.Date.AddDays(-back);
            var at = back == 0 ? now : EndOfDay(day);
            if (MinAt(urls, currency, at) is { } price) result.Add((DateOnly.FromDateTime(day), price));
        }
        return result;
    }

    /// <summary>
    /// The lowest and highest product price from <paramref name="from"/> to <paramref name="now"/>.
    /// The price only changes at a point or when a URL stops holding its price, so those instants
    /// are the only ones to look at. Null when there is no price in the window.
    /// </summary>
    public static (decimal Min, decimal Max)? Range(
        IReadOnlyCollection<UrlSeries> urls, string currency, DateTime from, DateTime now)
    {
        var instants = new List<DateTime> { from };
        foreach (var url in urls)
        {
            instants.AddRange(url.Points.Select(p => p.At).Where(at => at > from && at <= now));
            if (!url.ReachesNow && url.Points.Count > 0 && EndOfDay(url.Points[^1].At) is var end &&
                end > from && end < now)
            {
                instants.Add(end.AddTicks(1));
            }
        }

        (decimal Min, decimal Max)? range = null;
        foreach (var at in instants)
        {
            if (MinAt(urls, currency, at) is not { } price) continue;
            range = range is { } r ? (Math.Min(r.Min, price), Math.Max(r.Max, price)) : (price, price);
        }
        return range;
    }

    private static Point? HeldAt(UrlSeries url, DateTime at)
    {
        Point? held = null;
        foreach (var point in url.Points)
        {
            if (point.At > at) break;
            held = point;
        }

        if (held is null) return null;
        if (!url.ReachesNow && at > EndOfDay(url.Points[^1].At)) return null;
        return held;
    }

    private static DateTime EndOfDay(DateTime at) => at.Date.AddDays(1).AddTicks(-1);
}
