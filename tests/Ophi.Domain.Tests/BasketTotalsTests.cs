using FluentAssertions;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Services;

namespace Ophi.Domain.Tests;

public class BasketTotalsTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static ProductUrl Url(string host, decimal? price, string currency = "EUR",
        ProductUrlStatus status = ProductUrlStatus.Active, bool outOfStock = false) =>
        new()
        {
            Id = Guid.NewGuid(), Url = $"https://www.{host}/p/{Guid.NewGuid():N}", CurrentPrice = price,
            Currency = currency, Status = status, IsOutOfStock = outOfStock
        };

    private static BasketTotals.Item Item(Guid productId, int quantity, params ProductUrl[] urls) =>
        new(productId, quantity, urls);

    private static Func<string, ShippingRate?> Rates(params (string Host, decimal Amount, decimal? FreeAbove)[] rates) =>
        host => rates.Where(r => r.Host == host)
            .Select(r => new ShippingRate { Id = Guid.NewGuid(), Host = r.Host, Amount = r.Amount, Currency = "EUR", FreeAbove = r.FreeAbove })
            .FirstOrDefault();

    private static readonly Func<string, ShippingRate?> NoRates = _ => null;

    [Fact]
    public void Compute_OneStoreWithEveryItem_AddsShippingOnce()
    {
        var result = BasketTotals.Compute(
            [Item(A, 1, Url("shop.example", 10m)), Item(B, 1, Url("shop.example", 20m))],
            Rates(("shop.example", 4m, null)));

        var store = result.Stores.Single();
        store.Host.Should().Be("shop.example");
        store.Currency.Should().Be("EUR");
        store.Subtotal.Should().Be(30m);
        store.Shipping.Should().Be(4m);
        store.Total.Should().Be(34m);
        store.Complete.Should().BeTrue();
        store.MissingCount.Should().Be(0);
        result.BestStoreHost.Should().Be("shop.example");
    }

    [Fact]
    public void Compute_FreeAboveThreshold_AppliesToTheStoreSubtotal()
    {
        // Neither item reaches 25 alone; together they do.
        var result = BasketTotals.Compute(
            [Item(A, 1, Url("shop.example", 15m)), Item(B, 1, Url("shop.example", 15m))],
            Rates(("shop.example", 4m, 25m)));

        result.Stores.Single().Shipping.Should().Be(0m);
        result.Stores.Single().Total.Should().Be(30m);
    }

    [Fact]
    public void Compute_Quantity_MultipliesThePriceNotTheShipping()
    {
        var result = BasketTotals.Compute([Item(A, 3, Url("shop.example", 10m))], Rates(("shop.example", 4m, null)));

        result.Stores.Single().Subtotal.Should().Be(30m);
        result.Stores.Single().Total.Should().Be(34m);
    }

    [Fact]
    public void Compute_StoreMissingAnItem_IsIncompleteAndNeverBest()
    {
        // The partial store is cheaper, but it lacks B.
        var result = BasketTotals.Compute(
            [Item(A, 1, Url("cheap.example", 5m), Url("full.example", 10m)), Item(B, 1, Url("full.example", 20m))],
            NoRates);

        var cheap = result.Stores.Single(s => s.Host == "cheap.example");
        cheap.Complete.Should().BeFalse();
        cheap.MissingCount.Should().Be(1);
        cheap.Subtotal.Should().Be(5m);
        cheap.Items.Single(i => i.ProductId == B).Price.Should().BeNull();
        result.BestStoreHost.Should().Be("full.example");
        result.Stores[0].Host.Should().Be("full.example", "complete stores come first");
    }

    [Fact]
    public void Compute_PausedOutOfStockAndUnpricedUrls_DoNotCount()
    {
        var result = BasketTotals.Compute(
            [Item(A, 1,
                Url("shop.example", 5m, status: ProductUrlStatus.Paused),
                Url("shop.example", 6m, outOfStock: true),
                Url("shop.example", null),
                Url("shop.example", 9m))],
            NoRates);

        result.Stores.Single().Items.Single().Price.Should().Be(9m);
    }

    [Fact]
    public void Compute_HostWithOnlyDeadUrls_IsNotAStore()
    {
        var result = BasketTotals.Compute(
            [Item(A, 1, Url("gone.example", 5m, outOfStock: true), Url("shop.example", 9m))], NoRates);

        result.Stores.Select(s => s.Host).Should().Equal("shop.example");
    }

    [Fact]
    public void Compute_ItemWithTwoUrlsAtOneHost_TakesTheCheaper()
    {
        var result = BasketTotals.Compute([Item(A, 1, Url("shop.example", 12m), Url("shop.example", 9m))], NoRates);

        result.Stores.Single().Items.Single().Price.Should().Be(9m);
    }

    [Fact]
    public void Compute_SplitCheaperThanTheBestStore_TakesEachItemsCheapestUrl()
    {
        var result = BasketTotals.Compute(
            [Item(A, 1, Url("one.example", 10m), Url("two.example", 14m)),
             Item(B, 1, Url("one.example", 25m), Url("two.example", 20m))],
            NoRates);

        result.BestStoreHost.Should().Be("two.example"); // one = 35, two = 34
        result.Split!.Lines.Should().BeEquivalentTo(new[]
        {
            new BasketTotals.SplitLine(A, "one.example", 10m),
            new BasketTotals.SplitLine(B, "two.example", 20m)
        });
        result.Split.Total.Should().Be(30m);
        result.Split.Complete.Should().BeTrue();
    }

    [Fact]
    public void Compute_SplitWithTwoShippingCosts_CanCostMoreThanTheBestStore()
    {
        var result = BasketTotals.Compute(
            [Item(A, 1, Url("one.example", 10m), Url("two.example", 11m)),
             Item(B, 1, Url("one.example", 21m), Url("two.example", 20m))],
            Rates(("one.example", 5m, null), ("two.example", 5m, null)));

        var best = result.Stores.Single(s => s.Host == result.BestStoreHost);
        best.Total.Should().Be(36m);
        result.Split!.Shipping.Should().BeEquivalentTo(new[]
        {
            new BasketTotals.HostShipping("one.example", 5m),
            new BasketTotals.HostShipping("two.example", 5m)
        });
        result.Split.Total.Should().Be(40m);
    }

    [Fact]
    public void Compute_StoreWithMixedCurrencies_HasAnUnknownTotal()
    {
        var result = BasketTotals.Compute(
            [Item(A, 1, Url("shop.example", 10m, "EUR")), Item(B, 1, Url("shop.example", 12m, "USD"))],
            NoRates);

        var store = result.Stores.Single();
        store.Currency.Should().BeNull();
        store.Subtotal.Should().BeNull();
        store.Total.Should().BeNull();
        result.BestStoreHost.Should().BeNull();
    }

    [Fact]
    public void Compute_BestStore_IsChosenOnlyInTheDominantCurrency()
    {
        // Two complete EUR stores and one cheaper-looking USD store: no conversion, EUR dominates.
        var result = BasketTotals.Compute(
            [Item(A, 1, Url("eu1.example", 30m), Url("eu2.example", 25m), Url("us.example", 5m, "USD"))],
            NoRates);

        result.BestStoreHost.Should().Be("eu2.example");
    }

    [Fact]
    public void Compute_ShippingRateInAnotherCurrency_MakesTheTotalUnknown()
    {
        ShippingRate? Usd(string host) => new() { Id = Guid.NewGuid(), Host = host, Amount = 3m, Currency = "USD" };

        var result = BasketTotals.Compute([Item(A, 1, Url("shop.example", 10m))], Usd);

        result.Stores.Single().Subtotal.Should().Be(10m);
        result.Stores.Single().Shipping.Should().BeNull();
        result.Stores.Single().Total.Should().BeNull();
        result.BestStoreHost.Should().BeNull();
    }

    [Fact]
    public void Compute_ItemWithNoUrlInTheDominantCurrency_MakesTheSplitIncomplete()
    {
        var result = BasketTotals.Compute(
            [Item(A, 1, Url("shop.example", 10m)), Item(B, 1, Url("shop.example", 11m)), Item(Guid.NewGuid(), 1, Url("us.example", 5m, "USD"))],
            NoRates);

        result.Split!.Currency.Should().Be("EUR");
        result.Split.Complete.Should().BeFalse();
        result.Split.Lines.Should().HaveCount(3);
        result.Split.Lines.Count(l => l.Host == null).Should().Be(1);
    }

    [Fact]
    public void Compute_EqualPricesAtTwoHosts_BreakOnTheHostName()
    {
        var result = BasketTotals.Compute([Item(A, 1, Url("b.example", 10m), Url("a.example", 10m))], NoRates);

        result.BestStoreHost.Should().Be("a.example");
        result.Split!.Lines.Single().Host.Should().Be("a.example");
    }

    [Fact]
    public void Compute_EmptyBasket_HasNoStoresAndNoSplit()
    {
        var result = BasketTotals.Compute([], NoRates);

        result.Stores.Should().BeEmpty();
        result.BestStoreHost.Should().BeNull();
        result.Split.Should().BeNull();
    }
}
