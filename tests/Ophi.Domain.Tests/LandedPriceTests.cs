using FluentAssertions;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Services;

namespace Ophi.Domain.Tests;

public class LandedPriceTests
{
    private static ShippingRate Rate(decimal amount, string currency = "EUR", decimal? freeAbove = null) =>
        new() { Id = Guid.NewGuid(), Host = "shop.example", Amount = amount, Currency = currency, FreeAbove = freeAbove };

    [Fact]
    public void Shipping_WithNoRate_IsZero()
    {
        LandedPrice.Shipping(18.99m, "EUR", null).Should().Be(0m);
    }

    [Fact]
    public void Shipping_WithAFlatRate_IsTheAmount()
    {
        LandedPrice.Shipping(18.99m, "EUR", Rate(3.00m)).Should().Be(3.00m);
    }

    [Fact]
    public void Shipping_BelowTheFreeAboveThreshold_IsTheAmount()
    {
        LandedPrice.Shipping(29.99m, "EUR", Rate(3.00m, freeAbove: 30m)).Should().Be(3.00m);
    }

    [Fact]
    public void Shipping_AtTheFreeAboveThreshold_IsZero()
    {
        LandedPrice.Shipping(30m, "EUR", Rate(3.00m, freeAbove: 30m)).Should().Be(0m);
    }

    [Fact]
    public void Shipping_AboveTheFreeAboveThreshold_IsZero()
    {
        LandedPrice.Shipping(45m, "EUR", Rate(3.00m, freeAbove: 30m)).Should().Be(0m);
    }

    [Fact]
    public void Shipping_InAnotherCurrency_IsUnknown()
    {
        // Rates are display-only (agent-notes § Pricing): a USD shipping cost is never converted
        // to add to a EUR price.
        LandedPrice.Shipping(18.99m, "EUR", Rate(3.00m, currency: "USD")).Should().BeNull();
    }

    [Fact]
    public void Shipping_ComparesCurrenciesIgnoringCase()
    {
        LandedPrice.Shipping(18.99m, "eur", Rate(3.00m, currency: "EUR")).Should().Be(3.00m);
    }

    [Fact]
    public void Of_AddsTheShippingToThePrice()
    {
        LandedPrice.Of(18.99m, "EUR", Rate(3.00m)).Should().Be(21.99m);
    }

    [Fact]
    public void Of_InAnotherCurrency_IsUnknown()
    {
        LandedPrice.Of(18.99m, "EUR", Rate(3.00m, currency: "GBP")).Should().BeNull();
    }
}

public class LandedPriceForProductTests
{
    private static ProductUrl Url(string url, decimal? price, string currency = "EUR",
        ProductUrlStatus status = ProductUrlStatus.Active) =>
        new() { Id = Guid.NewGuid(), Url = url, CurrentPrice = price, Currency = currency, Status = status };

    private static Product ProductWith(params ProductUrl[] urls) =>
        new() { Id = Guid.NewGuid(), Currency = "EUR", CurrentPrice = urls.Where(u => u.CurrentPrice != null).Min(u => u.CurrentPrice), ProductUrls = urls.ToList() };

    private static Func<string, ShippingRate?> Rates(params ShippingRate[] rates) =>
        host => rates.FirstOrDefault(r => r.Host == host);

    private static ShippingRate Rate(string host, decimal amount, string currency = "EUR", decimal? freeAbove = null) =>
        new() { Id = Guid.NewGuid(), Host = host, Amount = amount, Currency = currency, FreeAbove = freeAbove };

    [Fact]
    public void ForProduct_WithNoRates_IsTheCheapestPriceWithZeroShipping()
    {
        var product = ProductWith(Url("https://a.example/1", 20m), Url("https://www.b.example/1", 18m));

        var landed = LandedPrice.ForProduct(product, Rates());

        landed.Should().Be(new LandedPrice.ProductLanded(18m, 0m, "b.example"));
    }

    [Fact]
    public void ForProduct_AFreeShippingStore_BeatsACheaperItemWithShipping()
    {
        var product = ProductWith(Url("https://a.example/1", 18.99m), Url("https://b.example/1", 21m));

        var landed = LandedPrice.ForProduct(product, Rates(Rate("a.example", 5m)));

        landed.Should().Be(new LandedPrice.ProductLanded(21m, 0m, "b.example"));
    }

    [Fact]
    public void ForProduct_FreeAboveAppliesToTheItemPrice()
    {
        var product = ProductWith(Url("https://a.example/1", 30m));

        var landed = LandedPrice.ForProduct(product, Rates(Rate("a.example", 4m, freeAbove: 30m)));

        landed.Should().Be(new LandedPrice.ProductLanded(30m, 0m, "a.example"));
    }

    [Fact]
    public void ForProduct_ShippingInAnotherCurrencyOnEveryUrl_IsUnknown()
    {
        var product = ProductWith(Url("https://a.example/1", 18m));

        var landed = LandedPrice.ForProduct(product, Rates(Rate("a.example", 3m, currency: "USD")));

        landed.Should().BeNull();
    }

    [Fact]
    public void ForProduct_SkipsAUrlWhoseShippingIsUnknown()
    {
        // a.example is cheaper on its face, but its shipping is in USD and cannot be added.
        var product = ProductWith(Url("https://a.example/1", 10m), Url("https://b.example/1", 25m));

        var landed = LandedPrice.ForProduct(product, Rates(Rate("a.example", 3m, currency: "USD"), Rate("b.example", 2m)));

        landed.Should().Be(new LandedPrice.ProductLanded(27m, 2m, "b.example"));
    }

    [Fact]
    public void ForProduct_ExcludesPausedUrls_AsTheAggregatorDoes()
    {
        var product = ProductWith(
            Url("https://a.example/1", 5m, status: ProductUrlStatus.Paused),
            Url("https://b.example/1", 20m));

        var landed = LandedPrice.ForProduct(product, Rates());

        landed!.Value.Host.Should().Be("b.example");
    }

    [Fact]
    public void ForProduct_ExcludesUrlsInAnotherCurrencyThanTheProduct()
    {
        var product = ProductWith(Url("https://a.example/1", 5m, currency: "GBP"), Url("https://b.example/1", 20m));

        var landed = LandedPrice.ForProduct(product, Rates());

        landed!.Value.Price.Should().Be(20m);
    }

    [Fact]
    public void ForProduct_ExcludesUnpricedUrls()
    {
        var product = ProductWith(Url("https://a.example/1", null), Url("https://b.example/1", 20m));

        LandedPrice.ForProduct(product, Rates())!.Value.Host.Should().Be("b.example");
    }

    [Fact]
    public void ForProduct_EqualLandedPrices_BreakOnTheHost()
    {
        var product = ProductWith(Url("https://z.example/1", 20m), Url("https://a.example/1", 17m));

        var landed = LandedPrice.ForProduct(product, Rates(Rate("a.example", 3m)));

        landed!.Value.Host.Should().Be("a.example");
    }

    [Fact]
    public void ForProduct_WithNoPricedUrl_IsNull()
    {
        var product = new Product { Id = Guid.NewGuid(), Currency = "EUR", ProductUrls = [Url("https://a.example/1", null)] };

        LandedPrice.ForProduct(product, Rates()).Should().BeNull();
    }

    [Fact]
    public void ForUrl_ReportsShippingAndLandedForOneUrl()
    {
        var url = Url("https://www.a.example/1", 18m);

        LandedPrice.ForUrl(url, Rates(Rate("a.example", 3m)))
            .Should().Be(new LandedPrice.UrlLanded("a.example", 3m, 21m));
    }

    [Fact]
    public void ForUrl_WithUnknownShipping_HasNoLandedPrice()
    {
        var url = Url("https://a.example/1", 18m);

        LandedPrice.ForUrl(url, Rates(Rate("a.example", 3m, currency: "USD")))
            .Should().Be(new LandedPrice.UrlLanded("a.example", null, null));
    }

    [Fact]
    public void ForUrl_WithNoPrice_HasNoLandedPrice()
    {
        LandedPrice.ForUrl(Url("https://a.example/1", null), Rates())
            .Should().Be(new LandedPrice.UrlLanded("a.example", null, null));
    }
}
