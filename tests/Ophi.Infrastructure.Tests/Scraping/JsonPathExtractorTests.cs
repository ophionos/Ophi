using FluentAssertions;
using Ophi.Infrastructure.Scraping;

namespace Ophi.Infrastructure.Tests.Scraping;

public class JsonPathExtractorTests
{
    [Fact]
    public void Extract_WithSimplePricePath_ReturnsPrice()
    {
        var jsonLd = """{"@type":"Product","name":"Widget","offers":{"price":"29.99"}}""";

        var result = JsonPathExtractor.Extract([jsonLd], "$.offers.price");

        result.Should().Be("29.99");
    }

    [Fact]
    public void Extract_WithOffersArray_ReturnsFirstPrice()
    {
        var jsonLd = """{"@type":"Product","offers":[{"price":"19.99"},{"price":"24.99"}]}""";

        var result = JsonPathExtractor.Extract([jsonLd], "$.offers[0].price");

        result.Should().Be("19.99");
    }

    [Fact]
    public void Extract_WithLowPrice_ReturnsLowPrice()
    {
        var jsonLd = """{"@type":"Product","offers":{"lowPrice":"14.99","highPrice":"29.99"}}""";

        var result = JsonPathExtractor.Extract([jsonLd], "$.offers.lowPrice");

        result.Should().Be("14.99");
    }

    [Fact]
    public void Extract_WithNamePath_ReturnsName()
    {
        var jsonLd = """{"@type":"Product","name":"Sony WH-1000XM5"}""";

        var result = JsonPathExtractor.Extract([jsonLd], "$.name");

        result.Should().Be("Sony WH-1000XM5");
    }

    [Fact]
    public void Extract_WithImagePath_ReturnsImageUrl()
    {
        var jsonLd = """{"@type":"Product","image":"https://example.com/product.jpg"}""";

        var result = JsonPathExtractor.Extract([jsonLd], "$.image");

        result.Should().Be("https://example.com/product.jpg");
    }

    [Fact]
    public void Extract_WithImageArray_ReturnsFirstImage()
    {
        var jsonLd = """{"@type":"Product","image":["https://example.com/1.jpg","https://example.com/2.jpg"]}""";

        var result = JsonPathExtractor.Extract([jsonLd], "$.image[0]");

        result.Should().Be("https://example.com/1.jpg");
    }

    [Fact]
    public void Extract_WithNoMatchingPath_ReturnsNull()
    {
        var jsonLd = """{"@type":"Product","name":"Widget"}""";

        var result = JsonPathExtractor.Extract([jsonLd], "$.offers.price");

        result.Should().BeNull();
    }

    [Fact]
    public void Extract_WithInvalidJsonPath_ReturnsNull()
    {
        var jsonLd = """{"@type":"Product","name":"Widget"}""";

        var result = JsonPathExtractor.Extract([jsonLd], "[[[invalid");

        result.Should().BeNull();
    }

    [Fact]
    public void Extract_WithMalformedJson_ReturnsNull()
    {
        var jsonLd = "not valid json at all {{{";

        var result = JsonPathExtractor.Extract([jsonLd], "$.name");

        result.Should().BeNull();
    }

    [Fact]
    public void Extract_WithEmptyJsonLdList_ReturnsNull()
    {
        var result = JsonPathExtractor.Extract([], "$.name");

        result.Should().BeNull();
    }

    [Fact]
    public void Extract_WithMultipleJsonLdBlocks_SearchesAll()
    {
        var block1 = """{"@type":"BreadcrumbList","itemListElement":[]}""";
        var block2 = """{"@type":"Product","name":"Widget","offers":{"price":"42.00"}}""";

        var result = JsonPathExtractor.Extract([block1, block2], "$.offers.price");

        result.Should().Be("42.00");
    }

    [Fact]
    public void Extract_WithGraphArray_NavigatesCorrectly()
    {
        var jsonLd = """{"@graph":[{"@type":"WebPage"},{"@type":"Product","name":"Widget","offers":{"price":"15.00"}}]}""";

        var result = JsonPathExtractor.Extract([jsonLd], "$['@graph'][1].offers.price");

        result.Should().Be("15.00");
    }

    [Fact]
    public void Extract_WithNumericPrice_ReturnsStringValue()
    {
        var jsonLd = """{"@type":"Product","offers":{"price":29.99}}""";

        var result = JsonPathExtractor.Extract([jsonLd], "$.offers.price");

        result.Should().Be("29.99");
    }

    [Fact]
    public void Extract_WithNullValue_ReturnsNull()
    {
        var jsonLd = """{"@type":"Product","offers":{"price":null}}""";

        var result = JsonPathExtractor.Extract([jsonLd], "$.offers.price");

        result.Should().BeNull();
    }

    [Fact]
    public void Extract_WithRootArray_NavigatesCorrectly()
    {
        var jsonLd = """[{"@type":"Product","offers":{"price":"10.00"}},{"@type":"Product","offers":{"price":"20.00"}}]""";

        var result = JsonPathExtractor.Extract([jsonLd], "$[0].offers.price");

        result.Should().Be("10.00");
    }

    [Fact]
    public void ExtractAll_WithMultiplePaths_ReturnsFirstMatch()
    {
        var jsonLd = """{"@type":"Product","offers":{"lowPrice":"14.99"}}""";

        var result = JsonPathExtractor.ExtractFirst(
            [jsonLd],
            ["$.offers.price", "$.offers.lowPrice", "$.offers[0].price"]);

        result.Should().Be("14.99");
    }

    [Fact]
    public void ExtractFirst_WithNoPaths_ReturnsNull()
    {
        var jsonLd = """{"@type":"Product","name":"Widget"}""";

        var result = JsonPathExtractor.ExtractFirst([jsonLd], []);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractAvailability_WithOutOfStockOffer_YieldsValue()
    {
        var jsonLd = """{"@type":"Product","offers":{"availability":"https://schema.org/OutOfStock"}}""";

        var result = JsonPathExtractor.ExtractAvailability([jsonLd]).ToList();

        result.Should().ContainSingle().Which.Should().Be("https://schema.org/OutOfStock");
    }

    [Fact]
    public void ExtractAvailability_WithOffersArray_FindsNestedValue()
    {
        var jsonLd = """{"@type":"Product","offers":[{"availability":"https://schema.org/InStock"}]}""";

        var result = JsonPathExtractor.ExtractAvailability([jsonLd]).ToList();

        result.Should().ContainSingle().Which.Should().Be("https://schema.org/InStock");
    }

    [Fact]
    public void ExtractAvailability_WithNoAvailabilityField_YieldsEmpty()
    {
        var jsonLd = """{"@type":"Product","offers":{"price":"19.99"}}""";

        var result = JsonPathExtractor.ExtractAvailability([jsonLd]).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractAvailability_WithMultipleBlocks_YieldsAllValuesAcrossBlocks()
    {
        // Regression test for multi-block JSON-LD: an OOS value in any block must surface,
        // even if an earlier block has an InStock value. Matches the regex-era semantics that
        // checked every script tag rather than stopping at the first match.
        var inStockBlock = """{"@type":"Product","offers":{"availability":"https://schema.org/InStock"}}""";
        var oosBlock = """{"@type":"Product","offers":{"availability":"https://schema.org/OutOfStock"}}""";

        var result = JsonPathExtractor.ExtractAvailability([inStockBlock, oosBlock]).ToList();

        result.Should().HaveCount(2);
        result.Should().Contain("https://schema.org/OutOfStock");
    }

    [Fact]
    public void ExtractCurrencyCode_WithThreeLetterCode_ReturnsUppercased()
    {
        var jsonLd = """{"@type":"Product","offers":{"priceCurrency":"eur"}}""";

        var result = JsonPathExtractor.ExtractCurrencyCode([jsonLd]);

        result.Should().Be("EUR");
    }

    [Fact]
    public void ExtractCurrencyCode_WithOffersArray_FindsCurrency()
    {
        var jsonLd = """{"@type":"Product","offers":[{"priceCurrency":"USD","price":"19.99"}]}""";

        var result = JsonPathExtractor.ExtractCurrencyCode([jsonLd]);

        result.Should().Be("USD");
    }

    [Fact]
    public void ExtractCurrencyCode_WithInvalidLengthCode_ReturnsNull()
    {
        var jsonLd = """{"@type":"Product","offers":{"priceCurrency":"DOLLAR"}}""";

        var result = JsonPathExtractor.ExtractCurrencyCode([jsonLd]);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractCurrencyCode_WithMissingField_ReturnsNull()
    {
        var jsonLd = """{"@type":"Product","name":"Widget"}""";

        var result = JsonPathExtractor.ExtractCurrencyCode([jsonLd]);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractCurrencyCode_WithInvalidThenValidAcrossBlocks_ReturnsValid()
    {
        // Regression test: a non-3-letter value in block 1 must not mask a valid code in block 2.
        var garbageBlock = """{"@type":"Product","offers":{"priceCurrency":"DOLLAR"}}""";
        var validBlock = """{"@type":"Product","offers":{"priceCurrency":"USD"}}""";

        var result = JsonPathExtractor.ExtractCurrencyCode([garbageBlock, validBlock]);

        result.Should().Be("USD");
    }

    [Fact]
    public void ExtractAll_WithMultipleMatches_YieldsAll()
    {
        var jsonLd = """{"@type":"Product","offers":[{"price":"1"},{"price":"2"},{"price":"3"}]}""";

        var result = JsonPathExtractor.ExtractAll([jsonLd], "$..price").ToList();

        result.Should().Equal("1", "2", "3");
    }

    #region Non-scalar matches

    [Fact]
    public void Extract_WithArrayValue_ReturnsNull()
    {
        // "[10,20]" stringified and handed to PriceParser loses its brackets, leaving "10,20",
        // which the European-comma heuristic reads as the decimal 10.20 — a fabricated price that
        // is neither bound. A price is always scalar; a structural node means "no match here".
        var jsonLd = """{"@type":"Product","offers":{"price":[10,20]}}""";

        var result = JsonPathExtractor.Extract([jsonLd], "$..price");

        result.Should().BeNull();
    }

    [Fact]
    public void Extract_WithObjectValue_ReturnsNull()
    {
        var jsonLd = """{"@type":"Product","offers":{"price":{"value":10,"currency":"EUR"}}}""";

        var result = JsonPathExtractor.Extract([jsonLd], "$..price");

        result.Should().BeNull();
    }

    [Fact]
    public void Extract_SkipsNonScalarMatch_AndReturnsLaterScalarInSameBlock()
    {
        // Extract previously inspected only Matches[0] and gave up on the whole block, so a
        // structural first match masked a perfectly good scalar alongside it.
        var jsonLd = """{"a":{"price":[1,2]},"b":{"price":9.99}}""";

        var result = JsonPathExtractor.Extract([jsonLd], "$..price");

        result.Should().Be("9.99");
    }

    [Fact]
    public void ExtractAll_OmitsNonScalarMatches()
    {
        var jsonLd = """{"a":{"price":[1,2]},"b":{"price":"5.50"},"c":{"price":{"v":1}}}""";

        var result = JsonPathExtractor.ExtractAll([jsonLd], "$..price").ToList();

        result.Should().Equal("5.50");
    }

    #endregion
}
