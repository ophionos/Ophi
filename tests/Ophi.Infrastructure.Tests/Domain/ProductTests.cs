using FluentAssertions;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Extensions;

namespace Ophi.Infrastructure.Tests.Domain;

public class ProductTests
{
    [Fact]
    public void MarkActive_FlipsStatusFromPendingToActive()
    {
        var product = new Product { Status = ProductStatus.Pending };

        product.MarkActive();

        product.Status.Should().Be(ProductStatus.Active);
    }

    [Fact]
    public void MarkActive_IsIdempotent()
    {
        var product = new Product { Status = ProductStatus.Active };

        product.MarkActive();

        product.Status.Should().Be(ProductStatus.Active);
    }

    [Fact]
    public void MarkAsError_FlipsToError()
    {
        var product = new Product { Status = ProductStatus.Active };

        product.MarkAsError();

        product.Status.Should().Be(ProductStatus.Error);
    }

    [Fact]
    public void Pause_FlipsToPaused()
    {
        var product = new Product { Status = ProductStatus.Active };

        product.Pause();

        product.Status.Should().Be(ProductStatus.Paused);
    }

    [Fact]
    public void AdoptCurrencyWhileUnpriced_WithoutAPrice_TakesTheCurrency()
    {
        var product = new Product { Currency = "USD", CurrentPrice = null };

        product.AdoptCurrencyWhileUnpriced("EUR");

        product.Currency.Should().Be("EUR");
    }

    [Fact]
    public void AdoptCurrencyWhileUnpriced_WithAPrice_KeepsTheCurrencyOfThatPrice()
    {
        // A priced product changes currency only through ProductPriceAggregator. Relabelling USD 50
        // as EUR 50 here also flipped every alert on the product into or out of dormancy.
        var product = new Product { Currency = "USD", CurrentPrice = 50m };

        product.AdoptCurrencyWhileUnpriced("EUR");

        product.Currency.Should().Be("USD");
    }

    [Fact]
    public void AdoptCurrencyWhileUnpriced_WithNullCurrency_KeepsTheCurrency()
    {
        var product = new Product { Currency = "GBP", CurrentPrice = null };

        product.AdoptCurrencyWhileUnpriced(null);

        product.Currency.Should().Be("GBP");
    }

    [Fact]
    public void GetPrimaryUrl_WhenCreatedAtTies_IsIndependentOfCollectionOrder()
    {
        // Bulk import can stamp several URLs in one SaveChanges; the "primary" link in alert emails
        // and the product page must not change with the order EF happens to load them in.
        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var a = new ProductUrl { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), CreatedAt = createdAt };
        var b = new ProductUrl { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), CreatedAt = createdAt };

        var first = new Product { ProductUrls = [a, b] }.GetPrimaryUrl();
        var second = new Product { ProductUrls = [b, a] }.GetPrimaryUrl();

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Resume_FlipsBackToActive()
    {
        var product = new Product { Status = ProductStatus.Paused };

        product.Resume();

        product.Status.Should().Be(ProductStatus.Active);
    }
}
