using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;
using RichardSzalay.MockHttp;

namespace Ophi.Infrastructure.Tests.Scraping;

/// <summary>
/// A challenge page answers with HTTP 200 and well-formed HTML, so status-code classification can
/// never catch it. Before this, the HTTP path had no challenge detection at all and such a fetch
/// ended as <see cref="ScrapeErrorCategory.ParseError"/> — "Could not extract price from page" —
/// which blames our parser for the site refusing us. Only the Playwright path checked, and only for
/// two Cloudflare titles.
///
/// <para>
/// The challenge pages here are synthetic: minimal documents carrying the title and URL signals
/// observed when probing from the deployment's real scrape egress (issue #131). They are not
/// captured HTML, so they prove the detection logic, not the fidelity of any particular vendor's
/// markup.
/// </para>
/// </summary>
public class AntiBotDetectionTests
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly ScrapingService _service;

    public AntiBotDetectionTests()
    {
        var loggerMock = new Mock<ILogger<ScrapingService>>();
        _mockHttp = new MockHttpMessageHandler();
        IStoreConfigProvider configProvider = new CodeStoreConfigProvider();
        _service = new ScrapingService(_mockHttp.ToHttpClient(), loggerMock.Object, configProvider);
    }

    [Theory]
    // PerimeterX, as served to a product-page request on walmart.com.
    [InlineData("Robot or human?")]
    // The two Cloudflare titles the Playwright path already knew about, now shared with this path.
    [InlineData("Just a moment...")]
    [InlineData("Attention Required! | Cloudflare")]
    public async Task ScrapeProductAsync_WhenTheTitleIsAChallenge_ReportsAntiBot(string title)
    {
        const string url = "https://www.example-shop.com/product/1";
        _mockHttp.When(url).Respond("text/html", ChallengePage(title));

        var result = await _service.ScrapeProductAsync(
            url, cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorCategory.Should().Be(
            ScrapeErrorCategory.AntiBot,
            "a challenge page is the site refusing us, not a page we failed to parse");
    }

    [Fact]
    public async Task ScrapeProductAsync_WhenRedirectedToABlockedPath_ReportsAntiBot()
    {
        // Walmart answers a blocked product request at /blocked?url=<base64 of the original path>.
        // Caught by path so the title can change without silently reopening the gap.
        const string url = "https://www.walmart.com/blocked?url=L2lwL3NvbWUtcHJvZHVjdA==";
        _mockHttp.When(url).Respond("text/html", ChallengePage("Access Denied"));

        var result = await _service.ScrapeProductAsync(
            url, cancellationToken: TestContext.Current.CancellationToken);

        result.ErrorCategory.Should().Be(ScrapeErrorCategory.AntiBot);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithANormalProductPage_DoesNotReportAntiBot()
    {
        // The control that matters most: detection must not fire on a page that scrapes fine.
        const string url = "https://www.example-shop.com/product/2";
        const string html = """
            <html>
                <head><title>Blue Widget — Example Shop</title></head>
                <body>
                    <h1 class='product-title'>Blue Widget</h1>
                    <span class='price'>$49.99</span>
                </body>
            </html>
            """;
        _mockHttp.When(url).Respond("text/html", html);

        var result = await _service.ScrapeProductAsync(
            url, cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Price.Should().Be(49.99m);
        result.ErrorCategory.Should().NotBe(ScrapeErrorCategory.AntiBot);
    }

    [Fact]
    public async Task ScrapeProductAsync_WhenThePageSimplyHasNoPrice_StillReportsParseError()
    {
        // The other direction: a genuine extraction failure must not be relabelled as a block, or
        // the new category becomes a dumping ground and stops meaning anything.
        const string url = "https://www.example-shop.com/product/3";
        const string html = """
            <html>
                <head><title>About Us — Example Shop</title></head>
                <body><p>We sell widgets.</p></body>
            </html>
            """;
        _mockHttp.When(url).Respond("text/html", html);

        var result = await _service.ScrapeProductAsync(
            url, cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.ParseError);
    }

    [Fact]
    public async Task ScrapeProductAsync_WhenAProductTitleMerelyContainsAChallengePhrase_DoesNotReportAntiBot()
    {
        // Forces exact-title matching. A substring check would strand this product forever, and the
        // user would see "blocked by anti-bot protection" for a page that loaded perfectly.
        const string url = "https://www.example-shop.com/product/4";
        const string html = """
            <html>
                <head><title>Robot or human? The Board Game — Example Shop</title></head>
                <body>
                    <h1 class='product-title'>Robot or human? The Board Game</h1>
                    <span class='price'>$29.99</span>
                </body>
            </html>
            """;
        _mockHttp.When(url).Respond("text/html", html);

        var result = await _service.ScrapeProductAsync(
            url, cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Price.Should().Be(29.99m);
        result.ErrorCategory.Should().NotBe(ScrapeErrorCategory.AntiBot);
    }

    [Fact]
    public async Task ScrapeWithConfigAsync_WhenTheTitleIsAChallenge_ReportsAntiBot()
    {
        // The sibling entry point, and the one where a parse-error message misleads most: it backs
        // the "test this store config" action (Features/Stores/TestStore.cs), so telling a user
        // "could not extract price" for a page that was never served sends them off tuning
        // selectors against a challenge page indefinitely.
        const string url = "https://www.example-shop.com/product/5";
        _mockHttp.When(url).Respond("text/html", ChallengePage("Robot or human?"));

        var result = await _service.ScrapeWithConfigAsync(
            url, TestStoreConfig(), TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.AntiBot);
    }

    [Fact]
    public async Task ScrapeWithConfigAsync_WithANormalProductPage_DoesNotReportAntiBot()
    {
        const string url = "https://www.example-shop.com/product/6";
        const string html = """
            <html>
                <head><title>Green Widget — Example Shop</title></head>
                <body><span class='price'>$19.99</span></body>
            </html>
            """;
        _mockHttp.When(url).Respond("text/html", html);

        var result = await _service.ScrapeWithConfigAsync(
            url, TestStoreConfig(), TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Price.Should().Be(19.99m);
    }

    private static StoreConfig TestStoreConfig() => new()
    {
        Id = "test-store",
        Name = "Test Store",
        DomainPatterns = ["example-shop.com"],
        Selectors = new StoreSelectorConfig
        {
            PriceSelectors = [".price"],
            NameSelectors = ["h1", "title"],
            ImageSelectors = ["meta[property='og:image']|content"]
        }
    };

    private static string ChallengePage(string title) =>
        $"""
        <html>
            <head><title>{title}</title></head>
            <body><p>Activate and hold the button to confirm that you are human.</p></body>
        </html>
        """;
}

/// <summary>
/// Direct coverage of the shared matcher. Worth having separately from the service tests because
/// <c>PlaywrightScrapingService</c> also consumes it and cannot be unit-tested — its use of these
/// signals is exercised only by the real-Chromium integration tier.
/// </summary>
public class AntiBotSignalsTests
{
    [Theory]
    [InlineData("Just a moment...")]
    [InlineData("Robot or human?")]
    [InlineData("Attention Required! | Cloudflare")]
    // Case and surrounding whitespace vary between vendors and renderings.
    [InlineData("robot or human?")]
    [InlineData("  Just a moment...  ")]
    public void IsChallengeTitle_ForAKnownInterstitial_IsTrue(string title) =>
        AntiBotSignals.IsChallengeTitle(title).Should().BeTrue();

    [Theory]
    // A product whose name happens to contain a challenge phrase must stay scrapeable.
    [InlineData("Robot or human? The Board Game")]
    [InlineData("Just a moment... — a novel")]
    // Partial matches are not matches.
    [InlineData("Robot")]
    [InlineData("Cloudflare")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsChallengeTitle_ForAnythingElse_IsFalse(string? title) =>
        AntiBotSignals.IsChallengeTitle(title).Should().BeFalse();

    [Theory]
    [InlineData("https://www.walmart.com/blocked")]
    [InlineData("https://www.walmart.com/blocked?url=L2lwL3g=&uuid=abc")]
    [InlineData("https://www.walmart.com/blocked/")]
    [InlineData("https://www.walmart.com/BLOCKED")]
    public void IsChallengeUrl_ForABlockRedirect_IsTrue(string url) =>
        AntiBotSignals.IsChallengeUrl(url).Should().BeTrue();

    [Theory]
    // The false positive the whole-path rule exists to prevent: "blocked" inside a product slug.
    [InlineData("https://www.example-shop.com/ip/noise-blocked-earplugs/12345")]
    [InlineData("https://www.example-shop.com/blocked-items/12345")]
    [InlineData("https://www.example-shop.com/product/blocked/12345")]
    [InlineData("https://www.example-shop.com/")]
    // Unparseable input is not evidence of a block; guessing here would invent them.
    [InlineData("not a url")]
    // Passes on Windows for the wrong reason and fails on Linux without the scheme check: a bare
    // POSIX path parses as an absolute file:// URI there, and production runs on Linux.
    [InlineData("/blocked")]
    [InlineData("file:///blocked")]
    [InlineData(null)]
    public void IsChallengeUrl_ForAnythingElse_IsFalse(string? url) =>
        AntiBotSignals.IsChallengeUrl(url).Should().BeFalse();

    [Fact]
    public void IsChallenge_FiresOnEitherSignalAlone()
    {
        // Each signal has to stand on its own — the Playwright path reached a block URL under an
        // unlisted title and reported it as clear, because only the title was ever consulted.
        AntiBotSignals.IsChallenge("Robot or human?", "https://www.example-shop.com/product/1")
            .Should().BeTrue("the title alone is enough");
        AntiBotSignals.IsChallenge("Access Denied", "https://www.walmart.com/blocked?url=x")
            .Should().BeTrue("the URL alone is enough");
        AntiBotSignals.IsChallenge("Blue Widget", "https://www.example-shop.com/product/1")
            .Should().BeFalse();
    }
}
