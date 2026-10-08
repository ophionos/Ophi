using FluentAssertions;
using Ophi.Domain.Entities;
using Ophi.Domain.Services;

namespace Ophi.Infrastructure.Tests.Domain;

public class ProductPriceAggregatorTests
{
    private static ProductPriceAggregator.UrlPrice At(decimal price, string currency = "USD") => new(price, currency);

    [Fact]
    public void ApplyAggregate_SingleUrl_SetsProductPriceToThatUrl()
    {
        var product = new Product { CurrentPrice = null };

        ProductPriceAggregator.ApplyAggregate(product, [At(25m)]);

        product.CurrentPrice.Should().Be(25m);
        product.Currency.Should().Be("USD");
        product.PreviousPrice.Should().BeNull();
    }

    [Fact]
    public void ApplyAggregate_MultipleUrls_ChoosesMinimum()
    {
        var product = new Product { CurrentPrice = null };

        ProductPriceAggregator.ApplyAggregate(product, [At(40m), At(25m), At(60m)]);

        product.CurrentPrice.Should().Be(25m);
    }

    [Fact]
    public void ApplyAggregate_ShiftsCurrentToPrevious_WhenPriorPriceExisted()
    {
        var product = new Product { CurrentPrice = 50m, Currency = "USD" };

        ProductPriceAggregator.ApplyAggregate(product, [At(30m)]);

        product.PreviousPrice.Should().Be(50m);
        product.CurrentPrice.Should().Be(30m);
    }

    [Fact]
    public void ApplyAggregate_DoesNotSetPreviousPrice_WhenProductHadNoPrior()
    {
        var product = new Product { CurrentPrice = null, PreviousPrice = null };

        ProductPriceAggregator.ApplyAggregate(product, [At(10m)]);

        product.PreviousPrice.Should().BeNull();
        product.CurrentPrice.Should().Be(10m);
    }

    [Fact]
    public void ApplyAggregate_IgnoresUrlsInOtherCurrencies()
    {
        // Raw decimals are not comparable across currencies: EUR 30 is numerically smallest but is
        // not the cheapest listing, and letting it win silently re-denominates the product. Only
        // URLs priced in the product's own currency compete for the MIN.
        var product = new Product { Currency = "USD" };

        ProductPriceAggregator.ApplyAggregate(product, [At(50m, "USD"), At(30m, "EUR"), At(60m, "GBP")]);

        product.CurrentPrice.Should().Be(50m);
        product.Currency.Should().Be("USD");
    }

    [Fact]
    public void ApplyAggregate_MatchesCurrencyCaseInsensitively()
    {
        var product = new Product { Currency = "USD" };

        ProductPriceAggregator.ApplyAggregate(product, [At(40m, "usd"), At(30m, "EUR")]);

        product.CurrentPrice.Should().Be(40m);
        product.Currency.Should().Be("usd");
    }

    [Fact]
    public void ApplyAggregate_WhenNoUrlMatchesProductCurrency_ReAnchorsOnWholeSet()
    {
        // Every listing moved to another currency (e.g. all URLs re-pointed to a EU region). Leaving
        // the product priceless would be worse than re-anchoring it onto what is actually tracked.
        var product = new Product { Currency = "USD", CurrentPrice = 99m };

        ProductPriceAggregator.ApplyAggregate(product, [At(30m, "EUR"), At(60m, "GBP")]);

        product.CurrentPrice.Should().Be(30m);
        product.Currency.Should().Be("EUR");
    }

    [Fact]
    public void ApplyAggregate_WhenReAnchoring_PicksTheDominantCurrency_NotTheSmallestNumber()
    {
        // The re-anchor path must not fall back into the very comparison this class exists to
        // prevent: GBP 20 is not "cheaper" than EUR 30 just because 20 < 30. Two of the three URLs
        // are EUR, so EUR is what the product is actually tracked in — MIN is then taken honestly
        // within EUR.
        var product = new Product { Currency = "USD", CurrentPrice = 99m };

        ProductPriceAggregator.ApplyAggregate(product, [At(30m, "EUR"), At(20m, "GBP"), At(35m, "EUR")]);

        product.Currency.Should().Be("EUR");
        product.CurrentPrice.Should().Be(30m);
    }

    [Fact]
    public void ApplyAggregate_WhenReAnchoringIsTied_IsIndependentOfListOrder()
    {
        // With no majority the choice is arbitrary, but it must be *stable*: the same set in a
        // different order must not re-denominate the product back and forth on alternating scrapes.
        var first = new Product { Currency = "USD", CurrentPrice = 99m };
        var second = new Product { Currency = "USD", CurrentPrice = 99m };

        ProductPriceAggregator.ApplyAggregate(first, [At(90m, "EUR"), At(20m, "GBP")]);
        ProductPriceAggregator.ApplyAggregate(second, [At(20m, "GBP"), At(90m, "EUR")]);

        second.Currency.Should().Be(first.Currency);
        second.CurrentPrice.Should().Be(first.CurrentPrice);
    }

    [Fact]
    public void ApplyAggregate_DoesNotOverwritePreviousPrice_WhenNewMinEqualsCurrent()
    {
        // No-op recheck: the cheapest URL re-scraped the same price. PreviousPrice must retain the
        // last DIFFERENT price so dashboard "% change" and the alert webhook OldPrice keep working.
        var product = new Product { CurrentPrice = 50m, PreviousPrice = 75m, Currency = "USD" };

        ProductPriceAggregator.ApplyAggregate(product, [At(50m)]);

        product.PreviousPrice.Should().Be(75m);
        product.CurrentPrice.Should().Be(50m);
    }

    [Fact]
    public void ApplyAggregate_WhenReAnchoring_ClearsPreviousPrice()
    {
        // USD 100 -> EUR 92 is not an 8% drop. PreviousPrice has no currency of its own, so a USD
        // baseline next to a EUR price fed a fake % change to the dashboard and the webhook OldPrice.
        var product = new Product { Currency = "USD", CurrentPrice = 100m, PreviousPrice = 120m };

        ProductPriceAggregator.ApplyAggregate(product, [At(92m, "EUR")]);

        product.Currency.Should().Be("EUR");
        product.CurrentPrice.Should().Be(92m);
        product.PreviousPrice.Should().BeNull();
    }

    [Fact]
    public void ApplyAggregate_WhenReAnchoringToTheSameNumber_StillClearsPreviousPrice()
    {
        // The no-op-recheck guard compares numbers only; USD 100 -> EUR 100 must not keep the
        // USD 120 baseline just because the number did not move.
        var product = new Product { Currency = "USD", CurrentPrice = 100m, PreviousPrice = 120m };

        ProductPriceAggregator.ApplyAggregate(product, [At(100m, "EUR")]);

        product.Currency.Should().Be("EUR");
        product.PreviousPrice.Should().BeNull();
    }

    [Fact]
    public void ApplyAggregate_WhenCurrencyDiffersOnlyInCase_KeepsPreviousPriceCapture()
    {
        var product = new Product { Currency = "USD", CurrentPrice = 50m };

        ProductPriceAggregator.ApplyAggregate(product, [At(40m, "usd")]);

        product.PreviousPrice.Should().Be(50m);
    }

    [Fact]
    public void ApplyAggregate_EmptyCollection_IsNoOp()
    {
        var product = new Product { CurrentPrice = 99m, Currency = "USD" };

        ProductPriceAggregator.ApplyAggregate(product, []);

        product.CurrentPrice.Should().Be(99m);
        product.Currency.Should().Be("USD");
    }

    [Fact]
    public void ApplyLiveAggregate_EmptyCollection_ClearsCurrentPrice()
    {
        // The caller passes every live URL, so an empty set means no live URL has a price.
        var product = new Product { CurrentPrice = 99m, Currency = "USD" };

        ProductPriceAggregator.ApplyLiveAggregate(product, []);

        product.CurrentPrice.Should().BeNull();
        product.Currency.Should().Be("USD");
    }

    [Fact]
    public void ApplyLiveAggregate_WithPrices_ChoosesMinimum()
    {
        var product = new Product { CurrentPrice = 20m, Currency = "USD" };

        ProductPriceAggregator.ApplyLiveAggregate(product, [At(40m), At(25m)]);

        product.CurrentPrice.Should().Be(25m);
        product.PreviousPrice.Should().Be(20m);
    }
}
