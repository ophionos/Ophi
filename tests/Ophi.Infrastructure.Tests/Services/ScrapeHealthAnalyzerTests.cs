using FluentAssertions;
using Ophi.Worker.Services;

namespace Ophi.Infrastructure.Tests.Services;

public class ScrapeHealthAnalyzerTests
{
    [Fact]
    public void Analyze_SameUrl_ReturnsNotSuspicious()
    {
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://example.com/products/123",
            "https://example.com/products/123",
            100m, 95m);

        result.IsSuspicious.Should().BeFalse();
    }

    [Fact]
    public void Analyze_SameUrlWithTrailingSlash_ReturnsNotSuspicious()
    {
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://example.com/products/123",
            "https://example.com/products/123/",
            100m, 95m);

        result.IsSuspicious.Should().BeFalse();
    }

    [Fact]
    public void Analyze_SameUrlWithQueryParams_ReturnsNotSuspicious()
    {
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://example.com/products/123",
            "https://example.com/products/123?ref=home",
            100m, 95m);

        result.IsSuspicious.Should().BeFalse();
    }

    [Fact]
    public void Analyze_DifferentDomain_ReturnsSuspicious()
    {
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://store.example.com/products/123",
            "https://other-site.com/products/123",
            100m, 95m);

        result.IsSuspicious.Should().BeTrue();
        result.Reason.Should().Contain("different domain");
    }

    [Fact]
    public void Analyze_RedirectToHomepage_ReturnsSuspicious()
    {
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://example.com/products/123",
            "https://example.com/",
            100m, 95m);

        result.IsSuspicious.Should().BeTrue();
        result.Reason.Should().Contain("homepage");
    }

    [Fact]
    public void Analyze_DeepToShallowPath_ReturnsSuspicious()
    {
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://example.com/products/electronics/123",
            "https://example.com/products",
            100m, 95m);

        result.IsSuspicious.Should().BeTrue();
        result.Reason.Should().Contain("shallower path");
    }

    [Fact]
    public void Analyze_NullFinalUrl_ReturnsNotSuspicious()
    {
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://example.com/products/123",
            null,
            100m, 95m);

        result.IsSuspicious.Should().BeFalse();
    }

    [Fact]
    public void Analyze_PriceAnomalyAboveThreshold_ReturnsSuspicious()
    {
        // 100 -> 10 is a 90% change, above 70% threshold
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://example.com/products/123",
            "https://example.com/products/123",
            100m, 10m);

        result.IsSuspicious.Should().BeTrue();
        result.Reason.Should().Contain("Price changed");
    }

    [Fact]
    public void Analyze_PriceAnomaly_IncreaseAboveThreshold_ReturnsSuspicious()
    {
        // 100 -> 200 is a 100% change, above 70% threshold
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://example.com/products/123",
            "https://example.com/products/123",
            100m, 200m);

        result.IsSuspicious.Should().BeTrue();
        result.Reason.Should().Contain("Price changed");
    }

    [Fact]
    public void Analyze_PriceChangeBelowThreshold_ReturnsNotSuspicious()
    {
        // 100 -> 60 is a 40% change, below 70% threshold
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://example.com/products/123",
            "https://example.com/products/123",
            100m, 60m);

        result.IsSuspicious.Should().BeFalse();
    }

    [Fact]
    public void Analyze_NullPreviousPrice_SkipsAnomalyCheck()
    {
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://example.com/products/123",
            "https://example.com/products/123",
            null, 10m);

        result.IsSuspicious.Should().BeFalse();
    }

    [Fact]
    public void Analyze_ZeroPreviousPrice_SkipsAnomalyCheck()
    {
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://example.com/products/123",
            "https://example.com/products/123",
            0m, 10m);

        result.IsSuspicious.Should().BeFalse();
    }

    [Fact]
    public void Analyze_RedirectTakesPrecedenceOverPriceAnomaly()
    {
        // Both redirect and price anomaly present — redirect reason should be returned
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://example.com/products/123",
            "https://other.com/",
            100m, 10m);

        result.IsSuspicious.Should().BeTrue();
        result.Reason.Should().Contain("different domain");
    }

    [Fact]
    public void Analyze_CustomThreshold_RespectedForPriceAnomaly()
    {
        // 100 -> 60 is 40% change. With 0.3 threshold, this is suspicious
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://example.com/products/123",
            "https://example.com/products/123",
            100m, 60m,
            priceAnomalyThreshold: 0.3m);

        result.IsSuspicious.Should().BeTrue();
    }

    [Fact]
    public void Analyze_SingleSegmentPath_NotSuspiciousWhenNotHomepage()
    {
        // /products → /category is same depth, not suspicious
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://example.com/products",
            "https://example.com/category",
            100m, 95m);

        result.IsSuspicious.Should().BeFalse();
    }

    #region Soft-404 Detection

    [Fact]
    public void AnalyzeSoft404_WithPageNotFoundTitle_ReturnsSuspicious()
    {
        var result = ScrapeHealthAnalyzer.AnalyzeSoft404("Page Not Found - Store Name");

        result.IsSuspicious.Should().BeTrue();
        result.Reason.Should().Contain("Soft-404 detected");
    }

    [Fact]
    public void AnalyzeSoft404_With404InTitle_ReturnsSuspicious()
    {
        var result = ScrapeHealthAnalyzer.AnalyzeSoft404("Error 404");

        result.IsSuspicious.Should().BeTrue();
        result.Reason.Should().Contain("Soft-404 detected");
    }

    [Fact]
    public void AnalyzeSoft404_WithProductNotFoundTitle_ReturnsSuspicious()
    {
        var result = ScrapeHealthAnalyzer.AnalyzeSoft404("Product Not Found");

        result.IsSuspicious.Should().BeTrue();
        result.Reason.Should().Contain("Soft-404 detected");
    }

    [Fact]
    public void AnalyzeSoft404_WithNormalTitle_ReturnsNotSuspicious()
    {
        var result = ScrapeHealthAnalyzer.AnalyzeSoft404("Amazing Product - $49.99");

        result.IsSuspicious.Should().BeFalse();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void AnalyzeSoft404_WithNullTitle_ReturnsNotSuspicious()
    {
        var result = ScrapeHealthAnalyzer.AnalyzeSoft404(null);

        result.IsSuspicious.Should().BeFalse();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void AnalyzeSoft404_WithEmptyTitle_ReturnsNotSuspicious()
    {
        var result = ScrapeHealthAnalyzer.AnalyzeSoft404("");

        result.IsSuspicious.Should().BeFalse();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void Analyze_WithSoft404Title_TakesPrecedenceOverPriceAnomaly()
    {
        // Soft-404 title + normal redirect/price data — soft-404 should take precedence
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://example.com/products/123",
            "https://example.com/products/123",
            100m, 10m,
            pageTitle: "Page Not Found - Store Name");

        result.IsSuspicious.Should().BeTrue();
        result.Reason.Should().Contain("Soft-404 detected");
    }

    #endregion

    #region Host normalization

    [Theory]
    // A canonical apex <-> www redirect is the same site. Flagging it marks EVERY scrape
    // suspicious, and since SuspiciousCount only resets on a clean scrape it climbs until the URL
    // auto-pauses — silently removing a correctly-tracked URL from both scraping and pricing.
    [InlineData("https://example.com/products/123", "https://www.example.com/products/123")]
    [InlineData("https://www.example.com/products/123", "https://example.com/products/123")]
    [InlineData("https://EXAMPLE.com/products/123", "https://example.com/products/123")]
    public void Analyze_WwwVersusApexRedirect_IsNotSuspicious(string originalUrl, string finalUrl)
    {
        var result = ScrapeHealthAnalyzer.Analyze(originalUrl, finalUrl, 100m, 95m);

        result.IsSuspicious.Should().BeFalse();
    }

    [Fact]
    public void Analyze_DifferentSubdomain_IsStillSuspicious()
    {
        // Guard against over-normalizing: only "www." is noise, other subdomains are real moves.
        var result = ScrapeHealthAnalyzer.Analyze(
            "https://store.example.com/products/123",
            "https://checkout.example.com/products/123",
            100m, 95m);

        result.IsSuspicious.Should().BeTrue();
        result.Reason.Should().Contain("different domain");
    }

    #endregion
}
