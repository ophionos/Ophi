using FluentAssertions;
using Ophi.Infrastructure.Scraping;

namespace Ophi.Infrastructure.Tests.Scraping;

public class PriceParserTests
{
    [Theory]
    // Portuguese/European locales (comma decimal, period thousands)
    [InlineData("649,00", "pt-PT", 649.00)]
    [InlineData("1.299,99", "pt-PT", 1299.99)]
    [InlineData("1.299,99 €", "pt-PT", 1299.99)]
    [InlineData("€1.299,99", "pt-PT", 1299.99)]
    [InlineData("10,50", "de-DE", 10.50)]
    [InlineData("999,90 €", "fr-FR", 999.90)]
    // US/UK locales (period decimal, comma thousands)
    [InlineData("1,299.99", "en-US", 1299.99)]
    [InlineData("$1,299.99", "en-US", 1299.99)]
    [InlineData("649.00", "en-US", 649.00)]
    [InlineData("£1,299.99", "en-GB", 1299.99)]
    // Null/default locale falls back to en-US behavior
    [InlineData("1,299.99", null, 1299.99)]
    [InlineData("649.00", null, 649.00)]
    public void ParsePrice_WithLocale_ReturnsCorrectAmount(string priceText, string? locale, decimal expectedAmount)
    {
        var result = PriceParser.Parse(priceText, locale);

        result.Should().NotBeNull();
        result.Value.Amount.Should().Be(expectedAmount);
    }

    [Theory]
    [InlineData("$99.99", "USD")]
    [InlineData("£49.99", "GBP")]
    [InlineData("€29.99", "EUR")]
    [InlineData("¥1000", "JPY")]
    [InlineData("99.99", "USD")] // default
    public void ParsePrice_DetectsCurrency(string priceText, string expectedCurrency)
    {
        var result = PriceParser.Parse(priceText);

        result.Should().NotBeNull();
        result.Value.Currency.Should().Be(expectedCurrency);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no price here")]
    public void ParsePrice_WithInvalidInput_ReturnsNull(string? priceText)
    {
        var result = PriceParser.Parse(priceText);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("649,00 €", "pt-PT", "EUR")]
    [InlineData("$1,299.99", "en-US", "USD")]
    [InlineData("£49,99", "en-GB", "GBP")]
    public void ParsePrice_WithLocaleAndCurrencySymbol_ReturnsCorrectCurrency(
        string priceText, string locale, string expectedCurrency)
    {
        var result = PriceParser.Parse(priceText, locale);

        result.Should().NotBeNull();
        result.Value.Currency.Should().Be(expectedCurrency);
    }

    [Fact]
    public void ParsePrice_SimpleInteger_ReturnsWholeNumber()
    {
        var result = PriceParser.Parse("649");

        result.Should().NotBeNull();
        result.Value.Amount.Should().Be(649m);
    }

    #region Auto-detection heuristic (comma-decimal without explicit locale)

    [Theory]
    // Comma + 1-2 trailing digits → European decimal (even with en-US locale)
    [InlineData("499,90", "en-US", 499.90)]
    [InlineData("EUR 499,90", "en-US", 499.90)]
    [InlineData("499,9", "en-US", 499.9)]
    [InlineData("1499,99", "en-US", 1499.99)]
    // Both separators present → last separator is decimal
    [InlineData("1.299,99", "en-US", 1299.99)]
    [InlineData("1,299.99", "pt-PT", 1299.99)]
    // Comma + 3 trailing digits → US thousands separator
    [InlineData("1,299", "en-US", 1299)]
    public void ParsePrice_AutoDetectsFormat_RegardlessOfLocale(string priceText, string locale, decimal expected)
    {
        var result = PriceParser.Parse(priceText, locale);

        result.Should().NotBeNull();
        result.Value.Amount.Should().Be(expected);
    }

    [Theory]
    // eBay-style European prices that were previously mishandled
    [InlineData("499,90 €", "en-US", 499.90)]
    [InlineData("€ 1.299,99", "en-US", 1299.99)]
    [InlineData("29,99 €", "en-US", 29.99)]
    public void ParsePrice_EbayEuropeanPrices_ParsedCorrectlyWithDefaultLocale(
        string priceText, string locale, decimal expected)
    {
        var result = PriceParser.Parse(priceText, locale);

        result.Should().NotBeNull();
        result.Value.Amount.Should().Be(expected);
    }

    [Theory]
    // Period with 1-2 trailing digits should be detected as decimal (US/UK format)
    // even when locale is European — fixes JSON-LD/content attribute values like "3.59"
    [InlineData("3.59", "pt-PT", 3.59)]
    [InlineData("649.00", "pt-PT", 649.00)]
    [InlineData("99.9", "de-DE", 99.9)]
    [InlineData("1234.50", "fr-FR", 1234.50)]
    public void ParsePrice_PeriodDecimalWithEuropeanLocale_DetectsAsUsUkFormat(
        string priceText, string locale, decimal expected)
    {
        var result = PriceParser.Parse(priceText, locale);

        result.Should().NotBeNull();
        result.Value.Amount.Should().Be(expected);
    }

    [Theory]
    // Period with 3 trailing digits remains ambiguous — locale decides
    // "1.299" with pt-PT → 1299 (period is thousands separator)
    [InlineData("1.299", "pt-PT", 1299)]
    // "1.299" with en-US → 1.299 (period is decimal separator)
    [InlineData("1.299", "en-US", 1.299)]
    public void ParsePrice_PeriodWithThreeTrailingDigits_DeferToLocale(
        string priceText, string locale, decimal expected)
    {
        var result = PriceParser.Parse(priceText, locale);

        result.Should().NotBeNull();
        result.Value.Amount.Should().Be(expected);
    }

    #endregion

    #region Non-positive values

    [Theory]
    // A savings/discount element ("-$15.00") or a placeholder ("$0.00") parses numerically but is
    // not a price. Returning null makes callers skip to the next selector instead of persisting a
    // negative or zero headline price (which would win the cross-URL MIN and fire every alert).
    [InlineData("-20.00", "en-US")]
    [InlineData("Save -$15.00", "en-US")]
    [InlineData("-1.299,99 €", "de-DE")]
    [InlineData("-0.01", "en-US")]
    [InlineData("$0.00", "en-US")]
    [InlineData("0", "en-US")]
    [InlineData("0,00 €", "pt-PT")]
    public void ParsePrice_WithNonPositiveValue_ReturnsNull(string priceText, string locale)
    {
        var result = PriceParser.Parse(priceText, locale);

        result.Should().BeNull();
    }

    [Fact]
    public void ParsePrice_WithSmallestPositiveValue_StillParses()
    {
        var result = PriceParser.Parse("0.01", "en-US");

        result.Should().NotBeNull();
        result.Value.Amount.Should().Be(0.01m);
    }

    #endregion
}
