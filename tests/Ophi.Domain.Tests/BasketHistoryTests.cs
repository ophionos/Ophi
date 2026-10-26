using FluentAssertions;
using Ophi.Domain.Entities;
using Ophi.Domain.Services;

namespace Ophi.Domain.Tests;

public class BasketHistoryTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = T0.AddDays(10);

    private static DateTime Day(int n) => T0.AddDays(n);

    private static BasketHistory.UrlHistory Url(string host, params (DateTime At, decimal Price)[] points) =>
        new($"https://www.{host}/p/{Guid.NewGuid():N}", points.Select(p => new BasketHistory.Price(p.At, p.Price, "EUR")).ToList(), true);

    private static BasketHistory.Item Item(Guid productId, int quantity, params BasketHistory.UrlHistory[] urls) =>
        new(productId, quantity, urls);

    private static Func<string, ShippingRate?> Rates(params (string Host, decimal Amount, decimal? FreeAbove)[] rates) =>
        host => rates.Where(r => r.Host == host)
            .Select(r => new ShippingRate { Id = Guid.NewGuid(), Host = r.Host, Amount = r.Amount, Currency = "EUR", FreeAbove = r.FreeAbove })
            .FirstOrDefault();

    private static readonly Func<string, ShippingRate?> NoRates = _ => null;

    private static IReadOnlyList<BasketHistory.Point> SeriesOf(IReadOnlyList<BasketHistory.StoreSeries> result, string host) =>
        result.Single(s => s.Host == host).Points;

    [Fact]
    public void Compute_PriceChangeOnOneItem_MovesOnlyThatHostsSeries()
    {
        var result = BasketHistory.Compute(
            [
                Item(A, 1, Url("one.example", (T0, 10m), (Day(3), 8m)), Url("two.example", (T0, 11m))),
                Item(B, 1, Url("one.example", (T0, 20m)), Url("two.example", (T0, 20m)))
            ],
            ["one.example", "two.example"], NoRates, Now);

        SeriesOf(result, "one.example").Should().Equal(
            new BasketHistory.Point(T0, 30m), new BasketHistory.Point(Day(3), 28m), new BasketHistory.Point(Now, 28m));
        SeriesOf(result, "two.example").Should().Equal(
            new BasketHistory.Point(T0, 31m), new BasketHistory.Point(Now, 31m));
    }

    [Fact]
    public void Compute_StoreGetsItsLastMissingItemLater_StartsItsSeriesThen()
    {
        var result = BasketHistory.Compute(
            [
                Item(A, 1, Url("one.example", (T0, 10m))),
                Item(B, 1, Url("one.example", (Day(4), 20m)))
            ],
            ["one.example"], NoRates, Now);

        SeriesOf(result, "one.example").Should().Equal(
            new BasketHistory.Point(Day(4), 30m), new BasketHistory.Point(Now, 30m));
    }

    [Fact]
    public void Compute_Shipping_IsAddedOncePerInstantAndFreeAboveUsesThatInstantsSubtotal()
    {
        // Subtotal 20 + 4 shipping; at day 2 the subtotal reaches the 25 threshold and shipping is free.
        var result = BasketHistory.Compute(
            [
                Item(A, 1, Url("one.example", (T0, 10m), (Day(2), 15m))),
                Item(B, 1, Url("one.example", (T0, 10m)))
            ],
            ["one.example"], Rates(("one.example", 4m, 25m)), Now);

        SeriesOf(result, "one.example").Should().Equal(
            new BasketHistory.Point(T0, 24m), new BasketHistory.Point(Day(2), 25m), new BasketHistory.Point(Now, 25m));
    }

    [Fact]
    public void Compute_Quantity_MultipliesTheItemPriceAtEveryInstant()
    {
        var result = BasketHistory.Compute(
            [Item(A, 3, Url("one.example", (T0, 10m), (Day(5), 9m)))],
            ["one.example"], NoRates, Now);

        SeriesOf(result, "one.example").Should().Equal(
            new BasketHistory.Point(T0, 30m), new BasketHistory.Point(Day(5), 27m), new BasketHistory.Point(Now, 27m));
    }

    [Fact]
    public void Compute_CheapestUrlOnAHost_IsTheItemPriceAtEachInstant()
    {
        // Two listings of one item at one host: the cheaper one at each instant counts.
        var result = BasketHistory.Compute(
            [Item(A, 1, Url("one.example", (T0, 10m)), Url("one.example", (T0, 12m), (Day(1), 7m)))],
            ["one.example"], NoRates, Now);

        SeriesOf(result, "one.example").Should().Equal(
            new BasketHistory.Point(T0, 10m), new BasketHistory.Point(Day(1), 7m), new BasketHistory.Point(Now, 7m));
    }

    [Fact]
    public void Compute_UrlNoLongerScraped_HoldsItsPriceOnlyToItsLastPoint()
    {
        // The cheap listing was paused after day 2, so from then on the store's other listing counts.
        var paused = new BasketHistory.UrlHistory("https://www.one.example/p/cheap",
            [new BasketHistory.Price(T0, 5m, "EUR"), new BasketHistory.Price(Day(2), 6m, "EUR")], ReachesNow: false);
        var result = BasketHistory.Compute(
            [Item(A, 1, paused, Url("one.example", (T0, 10m)))],
            ["one.example"], NoRates, Now);

        SeriesOf(result, "one.example").Should().Equal(
            new BasketHistory.Point(T0, 5m), new BasketHistory.Point(Day(2), 6m),
            new BasketHistory.Point(Day(2).AddTicks(1), 10m), new BasketHistory.Point(Now, 10m));
    }

    [Fact]
    public void Compute_OnlyTheRequestedHosts_InTheirOrder()
    {
        var result = BasketHistory.Compute(
            [Item(A, 1, Url("one.example", (T0, 10m)), Url("two.example", (T0, 9m)), Url("three.example", (T0, 8m)))],
            ["two.example", "one.example"], NoRates, Now);

        result.Select(s => (s.Host, s.Currency)).Should().Equal(("two.example", "EUR"), ("one.example", "EUR"));
    }
}
