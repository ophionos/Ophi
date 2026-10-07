using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;
using Ophi.Infrastructure.Scraping.Adapters.StoreConfigs;

namespace Ophi.Infrastructure.Tests.Scraping;

/// <summary>
/// Runs a <see cref="StoreConfig"/> against a saved page instead of a live fetch, so a store
/// adapter can be verified by anyone — including someone whose network can reach a site this
/// project's maintainers cannot (issue #136).
///
/// <para>
/// <b>To add a store:</b> save a real product page as
/// <c>Scraping/Fixtures/{store}-product.html</c>, set it to copy to the output directory, and add a
/// case here asserting the price, name and currency you can see on that page. A config whose
/// selectors do not match its own captured page fails here rather than in production.
/// </para>
///
/// <para>
/// <b>What this does and does not prove.</b> It proves a config's selectors agree with the page it
/// ships alongside and that the extraction pipeline honours them. It cannot prove the selectors
/// still match the live site — only a real scrape does that, and a fixture is a snapshot of the day
/// it was captured. Treat a passing fixture as "this config is internally coherent", not "this
/// store works".
/// </para>
/// </summary>
public class StoreConfigFixtureTests
{
    // The HttpClient is never used: ParseWithConfigAsync is the fetch-free half of the pipeline, which is
    // the entire reason a contributor without reach to a site can still verify its config.
    private readonly ScrapingService _service = new(
        new HttpClient(),
        NullLogger<ScrapingService>.Instance,
        new CodeStoreConfigProvider());

    [Fact]
    public async Task AmazonConfig_ExtractsPriceNameAndCurrency_FromItsFixture()
    {
        var result = await _service.ParseWithConfigAsync(
            LoadFixture("amazon-product.html"),
            AmazonConfig.Create(),
            "https://www.amazon.com/dp/B0TESTASIN",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Price.Should().Be(249.99m);
        result.Currency.Should().Be("USD");
        result.Name.Should().Be("Sennheiser HD 600 Open Back Headphone");
        result.StoreId.Should().Be("amazon");
    }

    [Fact]
    public async Task EbayConfig_ExtractsPriceNameAndCurrency_FromItsFixture()
    {
        var result = await _service.ParseWithConfigAsync(
            LoadFixture("ebay-product.html"),
            EbayConfig.Create(),
            "https://www.ebay.com/itm/123456789",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Price.Should().Be(189.50m);
        result.Currency.Should().Be("USD");
        result.Name.Should().Be("Sennheiser HD 600 Headphones - Refurbished");
        result.StoreId.Should().Be("ebay");
    }

    [Fact]
    public async Task AChallengePageInAFixture_IsReportedAsAntiBot_NotAsAParseError()
    {
        // The reason a fixture harness is safe to hand a contributor: if what they captured was the
        // block page rather than the product page, this says so instead of sending them off to
        // tune selectors against a page that was never served (the bug behind #134/#135).
        var result = await _service.ParseWithConfigAsync(
            LoadFixture("challenge-page.html"),
            AmazonConfig.Create(),
            "https://www.amazon.com/dp/B0TESTASIN",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.AntiBot);
    }

    [Fact]
    public async Task APageWithNoPrice_IsAParseError()
    {
        var result = await _service.ParseWithConfigAsync(
            "<html><head><title>Nothing here</title></head><body><p>No offer</p></body></html>",
            AmazonConfig.Create(),
            "https://www.amazon.com/dp/B0TESTASIN",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.ParseError);
    }

    private static string LoadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scraping", "Fixtures", name));
}
