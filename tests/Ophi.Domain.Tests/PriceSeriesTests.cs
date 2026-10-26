using FluentAssertions;
using Ophi.Domain.Services;

namespace Ophi.Domain.Tests;

public class PriceSeriesTests
{
    private static readonly DateTime Now = new(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc);

    private static PriceSeries.Point At(int daysAgo, decimal price, string currency = "EUR", int hour = 9) =>
        new(Now.Date.AddDays(-daysAgo).AddHours(hour), price, currency);

    private static PriceSeries.UrlSeries Url(bool reachesNow, params PriceSeries.Point[] points) => new(points, reachesNow);

    [Fact]
    public void Daily_AStablePrice_IsFlatForEveryDay()
    {
        var urls = new[] { Url(true, At(60, 20m)) };

        var daily = PriceSeries.Daily(urls, "EUR", Now, days: 30);

        daily.Should().HaveCount(30);
        daily.Select(d => d.Price).Should().AllBeEquivalentTo(20m);
        daily[0].Day.Should().Be(DateOnly.FromDateTime(Now.Date.AddDays(-29)));
        daily[^1].Day.Should().Be(DateOnly.FromDateTime(Now.Date));
    }

    [Fact]
    public void Daily_TwoUrlsChangingOnDifferentDays_IsTheMinOfTheCarriedPrices()
    {
        // a: 30 from day 5, 25 from day 2. b: 28 from day 4, 35 from day 1.
        var urls = new[]
        {
            Url(true, At(5, 30m), At(2, 25m)),
            Url(true, At(4, 28m), At(1, 35m))
        };

        var daily = PriceSeries.Daily(urls, "EUR", Now, days: 6);

        daily.Select(d => d.Price).Should().Equal(30m, 28m, 28m, 25m, 25m, 25m);
    }

    [Fact]
    public void Daily_DaysBeforeTheFirstPrice_HaveNoValue()
    {
        var daily = PriceSeries.Daily([Url(true, At(2, 10m))], "EUR", Now, days: 30);

        daily.Select(d => d.Day).Should().Equal(
            DateOnly.FromDateTime(Now.Date.AddDays(-2)),
            DateOnly.FromDateTime(Now.Date.AddDays(-1)),
            DateOnly.FromDateTime(Now.Date));
    }

    [Fact]
    public void Daily_TakesTheLastPriceOfTheDayForOneUrl()
    {
        var daily = PriceSeries.Daily([Url(true, At(1, 10m, hour: 8), At(1, 20m, hour: 9))], "EUR", Now, days: 2);

        daily.Select(d => d.Price).Should().Equal(20m, 20m);
    }

    [Fact]
    public void Daily_AUrlThatNoLongerReachesNow_EndsOnTheDayOfItsLastPrice()
    {
        // A paused URL is not scraped, so nothing says its last price still holds.
        var urls = new[] { Url(false, At(3, 5m)), Url(true, At(5, 20m)) };

        var daily = PriceSeries.Daily(urls, "EUR", Now, days: 4);

        daily.Select(d => d.Price).Should().Equal(5m, 20m, 20m, 20m);
    }

    [Fact]
    public void Daily_APriceInAnotherCurrency_TakesNoPart()
    {
        // The URL was re-pointed from GBP to EUR two days ago.
        var urls = new[] { Url(true, At(4, 9m, "GBP"), At(2, 12m)) };

        var daily = PriceSeries.Daily(urls, "EUR", Now, days: 5);

        daily.Select(d => d.Price).Should().Equal(12m, 12m, 12m);
    }

    [Fact]
    public void Range_IsTheMinAndMaxOfTheCarriedSeriesFromTheWindowStart()
    {
        // 10 held from before the window into it, then 40, then 100.
        var urls = new[] { Url(true, At(100, 10m), At(60, 40m), At(10, 100m)) };

        PriceSeries.Range(urls, "EUR", Now.AddDays(-90), Now).Should().Be((10m, 100m));
    }

    [Fact]
    public void Range_IsTheMaxOfTheProductPrice_NotOfOneUrl()
    {
        // b's 50 never was the product price: a held 20 throughout.
        var urls = new[] { Url(true, At(30, 20m)), Url(true, At(20, 50m), At(10, 25m)) };

        PriceSeries.Range(urls, "EUR", Now.AddDays(-90), Now).Should().Be((20m, 20m));
    }

    [Fact]
    public void Range_SeesTheRiseWhenAUrlStopsReachingNow()
    {
        var urls = new[] { Url(false, At(10, 5m)), Url(true, At(30, 20m)) };

        PriceSeries.Range(urls, "EUR", Now.AddDays(-90), Now).Should().Be((5m, 20m));
    }

    [Fact]
    public void Range_WithNoPrice_IsNull()
    {
        PriceSeries.Range([Url(true)], "EUR", Now.AddDays(-90), Now).Should().BeNull();
    }
}
