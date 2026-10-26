using FluentAssertions;
using Ophi.Domain.Entities;
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
