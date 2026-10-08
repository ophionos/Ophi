using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Moq;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;
using Ophi.Infrastructure.Scraping.Adapters.StoreConfigs;

namespace Ophi.Infrastructure.Tests.Scraping;

public class PlaywrightScrapingServiceTests
{
    private readonly Mock<IStoreConfigProvider> _configProviderMock;
    private readonly Mock<IPlaywrightBrowserManager> _browserManagerMock;
    private readonly Mock<IPage> _pageMock;
    private readonly Mock<IBrowserContext> _contextMock;
    private readonly PlaywrightScrapingService _service;

    /// <summary>What the start-URL pre-check resolves each host to. Unlisted hosts get a public address.</summary>
    private readonly Dictionary<string, IPAddress[]> _dns = new(StringComparer.OrdinalIgnoreCase);

    public PlaywrightScrapingServiceTests()
    {
        var loggerMock = new Mock<ILogger<PlaywrightScrapingService>>();
        _configProviderMock = new Mock<IStoreConfigProvider>();
        _browserManagerMock = new Mock<IPlaywrightBrowserManager>();
        _pageMock = new Mock<IPage>();
        _contextMock = new Mock<IBrowserContext>();

        // Wire up page -> context relationship
        _pageMock.Setup(x => x.Context).Returns(_contextMock.Object);

        // Default page title (non-challenge page)
        _pageMock.Setup(x => x.TitleAsync()).ReturnsAsync("Product Page");

        // Setup generic config
        _configProviderMock.Setup(x => x.GetGenericConfig())
            .Returns(new StoreConfig
            {
                Id = "generic",
                Name = "Generic Store",
                DomainPatterns = [],
                IsBuiltIn = true,
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = [".price", "[data-price]"],
                    NameSelectors = ["h1", ".product-name"],
                    ImageSelectors = ["img.product", "[property='og:image']|content"]
                }
            });

        _service = new PlaywrightScrapingService(
            loggerMock.Object,
            _configProviderMock.Object,
            _browserManagerMock.Object,
            (host, _) => Task.FromResult(_dns.GetValueOrDefault(host) ?? [IPAddress.Parse("93.184.215.14")]));
    }

    [Fact]
    public async Task ScrapeProductAsync_HostResolvesToBlockedAddress_ReturnsBlockedDestinationWithoutOpeningAPage()
    {
        const string url = "https://intranet.example/product";
        _dns["intranet.example"] = [IPAddress.Parse("10.0.0.5")];

        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.BlockedDestination);
        result.Error.Should().NotContain("10.0.0.5");
        _browserManagerMock.Verify(x => x.NewPageAsync(It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ScrapeWithConfigAsync_BlockedIpLiteral_ReturnsBlockedDestinationWithoutOpeningAPage()
    {
        var config = _configProviderMock.Object.GetGenericConfig();

        var result = await _service.ScrapeWithConfigAsync(
            "http://169.254.169.254/latest/meta-data", config, TestContext.Current.CancellationToken);

        result.ErrorCategory.Should().Be(ScrapeErrorCategory.BlockedDestination);
        _browserManagerMock.Verify(x => x.NewPageAsync(It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ScrapeProductAsync_MixedAnswer_IsNotRefusedByThePreCheck()
    {
        // Same rule as the proxy: a host with at least one allowed address is fetched (the proxy then
        // connects only to the allowed one).
        const string url = "https://mixed.example/product";
        _dns["mixed.example"] = [IPAddress.Parse("10.0.0.5"), IPAddress.Parse("93.184.215.14")];
        SetupPage(url, "$5.00", "Product");
        _configProviderMock.Setup(x => x.GetConfigForUrl(url)).Returns((StoreConfig?)null);

        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ScrapeProductAsync_NameDoesNotResolve_LeavesTheFailureToTheBrowser()
    {
        const string url = "https://gone.example/product";
        _dns["gone.example"] = [];
        _browserManagerMock.Setup(x => x.NewPageAsync(It.IsAny<string?>())).ReturnsAsync(_pageMock.Object);
        _pageMock.Setup(x => x.GotoAsync(url, It.IsAny<PageGotoOptions>()))
            .ThrowsAsync(new PlaywrightException("net::ERR_SOCKS_CONNECTION_FAILED"));
        _configProviderMock.Setup(x => x.GetConfigForUrl(url)).Returns((StoreConfig?)null);

        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        result.ErrorCategory.Should().NotBe(ScrapeErrorCategory.BlockedDestination);
    }

    [Fact]
    public async Task ScrapeProductAsync_ReturnsAllData_OnSuccess()
    {
        // Arrange
        const string url = "https://example.com/product";
        SetupPage(url, "$99.99", "Test Product", "https://example.com/image.jpg");

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(99.99m);
        result.Currency.Should().Be("USD");
        result.Name.Should().Be("Test Product");
        result.ImageUrl.Should().Be("https://example.com/image.jpg");
        result.StoreId.Should().Be("generic");
    }

    [Fact]
    public async Task ScrapeProductAsync_ReturnsFailure_OnNavigationTimeout()
    {
        // Arrange
        const string url = "https://example.com/product";
        _browserManagerMock.Setup(x => x.NewPageAsync(It.IsAny<string?>()))
            .ReturnsAsync(_pageMock.Object);

        _pageMock.Setup(x => x.GotoAsync(url, It.IsAny<PageGotoOptions>()))
            .ThrowsAsync(new TimeoutException("Navigation timed out"));

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Scraping error");
    }

    [Fact]
    public async Task ScrapeProductAsync_ReturnsFailure_WhenNoPriceFound()
    {
        // Arrange
        const string url = "https://example.com/product";
        SetupPage(url, priceText: null, name: "Test Product");

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Could not extract price from page");
    }

    [Fact]
    public async Task ScrapeProductAsync_ExtractsPrice_ViaCssSelector()
    {
        // Arrange
        const string url = "https://example.com/product";
        SetupPage(url, "$45.00", "Product");

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(45.00m);
    }

    [Fact]
    public async Task ScrapeProductAsync_NormalizesImageUrl_RelativeToAbsolute()
    {
        // Arrange
        const string url = "https://example.com/product";
        SetupPage(url, "$29.99", "Product", "/images/product.jpg");

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.ImageUrl.Should().Be("https://example.com/images/product.jpg");
    }

    [Fact]
    public async Task ScrapeProductAsync_NormalizesImageUrl_ProtocolRelative()
    {
        // Arrange
        const string url = "https://example.com/product";
        SetupPage(url, "$29.99", "Product", "//cdn.example.com/image.jpg");

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.ImageUrl.Should().Be("https://cdn.example.com/image.jpg");
    }

    [Fact]
    public async Task ScrapeProductAsync_ClosesContext_InFinallyBlock()
    {
        // Arrange
        const string url = "https://example.com/product";
        _browserManagerMock.Setup(x => x.NewPageAsync(It.IsAny<string?>()))
            .ReturnsAsync(_pageMock.Object);

        _pageMock.Setup(x => x.GotoAsync(url, It.IsAny<PageGotoOptions>()))
            .ThrowsAsync(new Exception("Navigation error"));

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        _contextMock.Verify(x => x.CloseAsync(It.IsAny<BrowserContextCloseOptions>()), Times.Once);
    }

    [Fact]
    public async Task ScrapeProductAsync_UsesCustomSelector_WhenProvided()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string customSelector = ".special-price";
        var priceElementMock = new Mock<IElementHandle>();

        _browserManagerMock.Setup(x => x.NewPageAsync(It.IsAny<string?>()))
            .ReturnsAsync(_pageMock.Object);

        _pageMock.Setup(x => x.GotoAsync(url, It.IsAny<PageGotoOptions>()))
            .ReturnsAsync((IResponse?)null);

        _pageMock.Setup(x => x.CloseAsync(It.IsAny<PageCloseOptions>()))
            .Returns(Task.CompletedTask);

        // Price extraction iterates all matches per selector; default every selector to no matches.
        _pageMock.Setup(x => x.QuerySelectorAllAsync(It.IsAny<string>()))
            .ReturnsAsync([]);

        // Setup custom selector to return price
        _pageMock.Setup(x => x.QuerySelectorAllAsync(customSelector))
            .ReturnsAsync([priceElementMock.Object]);
        priceElementMock.Setup(x => x.GetAttributeAsync("data-price"))
            .ReturnsAsync((string?)null);
        priceElementMock.Setup(x => x.GetAttributeAsync("content"))
            .ReturnsAsync((string?)null);
        priceElementMock.Setup(x => x.TextContentAsync())
            .ReturnsAsync("$75.00");

        // Setup name
        var nameElementMock = new Mock<IElementHandle>();
        _pageMock.Setup(x => x.QuerySelectorAsync("h1"))
            .ReturnsAsync(nameElementMock.Object);
        nameElementMock.Setup(x => x.TextContentAsync())
            .ReturnsAsync("Test Product");

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        // Act
        var result = await _service.ScrapeProductAsync(url, customSelector, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(75.00m);
        result.DetectedSelector.Should().Be(customSelector);
    }

    [Fact]
    public async Task ScrapeProductAsync_DetectsGBP_FromSymbol()
    {
        // Arrange
        const string url = "https://example.com/product";
        SetupPage(url, "£49.99", "Product");

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Currency.Should().Be("GBP");
    }

    [Fact]
    public async Task ScrapeProductAsync_DetectsEUR_FromSymbol()
    {
        // Arrange
        const string url = "https://example.com/product";
        SetupPage(url, "€59.99", "Product");

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task ScrapeProductAsync_SkipsEmptyFirstMatch_AndUsesFirstParseablePrice()
    {
        // Regression for the amazon.es bug: the first `.a-price .a-offscreen` is an empty
        // placeholder element, with the real price ("€17.91") in a later match. Using only
        // the first match abandoned the selector and fell through to `.a-price-whole`, which
        // yields "17." (integer part, no symbol) -> 17.0 USD. The extractor must skip the
        // empty match and use the first one that parses -> 17.91 EUR.
        const string url = "https://www.amazon.es/-/en/dp/1646093240/";
        _configProviderMock.Setup(x => x.GetConfigForUrl(url)).Returns(AmazonConfig.Create());

        _browserManagerMock.Setup(x => x.NewPageAsync(It.IsAny<string?>()))
            .ReturnsAsync(_pageMock.Object);
        _pageMock.Setup(x => x.GotoAsync(url, It.IsAny<PageGotoOptions>()))
            .ReturnsAsync((IResponse?)null);
        _pageMock.Setup(x => x.CloseAsync(It.IsAny<PageCloseOptions>()))
            .Returns(Task.CompletedTask);

        // Every selector defaults to no matches; name/image/currency selectors return null.
        _pageMock.Setup(x => x.QuerySelectorAllAsync(It.IsAny<string>())).ReturnsAsync([]);
        _pageMock.Setup(x => x.QuerySelectorAsync(It.IsAny<string>())).ReturnsAsync((IElementHandle?)null);
        // Amazon config has image regex patterns that read full page HTML when selectors miss.
        _pageMock.Setup(x => x.ContentAsync()).ReturnsAsync("<html></html>");

        var emptyOffscreen = new Mock<IElementHandle>();
        emptyOffscreen.Setup(e => e.GetAttributeAsync("data-price")).ReturnsAsync((string?)null);
        emptyOffscreen.Setup(e => e.GetAttributeAsync("content")).ReturnsAsync((string?)null);
        emptyOffscreen.Setup(e => e.TextContentAsync()).ReturnsAsync(""); // hidden placeholder

        var realOffscreen = new Mock<IElementHandle>();
        realOffscreen.Setup(e => e.GetAttributeAsync("data-price")).ReturnsAsync((string?)null);
        realOffscreen.Setup(e => e.GetAttributeAsync("content")).ReturnsAsync((string?)null);
        realOffscreen.Setup(e => e.TextContentAsync()).ReturnsAsync("€17.91");

        _pageMock.Setup(x => x.QuerySelectorAllAsync(".a-price .a-offscreen"))
            .ReturnsAsync([emptyOffscreen.Object, realOffscreen.Object]);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — the real EUR price, not 17.0 USD from `.a-price-whole`
        result.Success.Should().BeTrue();
        result.Price.Should().Be(17.91m);
        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task ScrapeProductAsync_TruncatesName_To500Chars()
    {
        // Arrange
        const string url = "https://example.com/product";
        var longName = new string('A', 600);
        SetupPage(url, "$29.99", longName);

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Name.Should().HaveLength(500);
    }

    // Issue #23: the Playwright generic path must hand back the rendered DOM so the worker can
    // auto-create a store config — but only when asked (captureHtml), so recurring scrapes and
    // store-config scrapes don't pay the extra ContentAsync round-trip.

    [Fact]
    public async Task ScrapeProductAsync_PopulatesFetchedHtml_WhenCaptureHtmlAndGeneric()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string renderedHtml = "<html><body><span class='price'>$99.99</span></body></html>";
        SetupPage(url, "$99.99", "Test Product");
        _pageMock.Setup(x => x.ContentAsync()).ReturnsAsync(renderedHtml);
        _configProviderMock.Setup(x => x.GetConfigForUrl(url)).Returns((StoreConfig?)null);

        // Act
        var result = await _service.ScrapeProductAsync(
            url, captureHtml: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — the rendered DOM is returned for the generic adapter
        result.Success.Should().BeTrue();
        result.StoreId.Should().Be("generic");
        result.FetchedHtml.Should().Be(renderedHtml);
    }

    [Fact]
    public async Task ScrapeProductAsync_LeavesFetchedHtmlNull_WhenCaptureHtmlFalse()
    {
        // Arrange
        const string url = "https://example.com/product";
        SetupPage(url, "$99.99", "Test Product");
        _pageMock.Setup(x => x.ContentAsync()).ReturnsAsync("<html>should not be read</html>");
        _configProviderMock.Setup(x => x.GetConfigForUrl(url)).Returns((StoreConfig?)null);

        // Act — captureHtml defaults to false (the recurring-scrape case)
        var result = await _service.ScrapeProductAsync(
            url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — no HTML captured, and no extra ContentAsync round-trip taken
        result.Success.Should().BeTrue();
        result.FetchedHtml.Should().BeNull();
        _pageMock.Verify(x => x.ContentAsync(), Times.Never);
    }

    [Fact]
    public async Task ScrapeProductAsync_LeavesFetchedHtmlNull_ForStoreConfigPath()
    {
        // Arrange — a store-specific config matches, so StoreId is never "generic" and auto-create
        // never runs; the HTML must not be captured even though captureHtml is requested.
        const string url = "https://shop.example.com/product";
        SetupPage(url, "$99.99", "Test Product");
        _pageMock.Setup(x => x.ContentAsync()).ReturnsAsync("<html>nope</html>");
        _configProviderMock.Setup(x => x.GetConfigForUrl(url)).Returns(new StoreConfig
        {
            Id = "myshop",
            Name = "My Shop",
            DomainPatterns = ["shop.example.com"],
            IsBuiltIn = true,
            Selectors = new StoreSelectorConfig
            {
                PriceSelectors = [".price"],
                NameSelectors = ["h1"],
                ImageSelectors = []
            }
        });

        // Act
        var result = await _service.ScrapeProductAsync(
            url, captureHtml: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.StoreId.Should().Be("myshop");
        result.FetchedHtml.Should().BeNull();
        _pageMock.Verify(x => x.ContentAsync(), Times.Never);
    }

    [Fact]
    public async Task ScrapeProductAsync_WhenItemCannotShipToLocation_RecordsUnavailableWithoutStrayPrice()
    {
        // Playwright twin of the amazon.com geo-block (the worker egresses an EU/PT IP): the buy box
        // says the item cannot ship to the viewer's locale, so the only price-like text on the page
        // is a stray accessory cell. The no-offer marker must take precedence over that stray price:
        // discard it and record the listing as unavailable (the observed `USD 11.16` bug).
        const string url = "https://www.amazon.com/dp/B0CHWRXH8B";
        SetupPage(url, "EUR 11.16", "Apple AirPods Pro (2nd Generation)");
        _pageMock.Setup(x => x.InnerTextAsync("body", It.IsAny<PageInnerTextOptions>()))
            .ReturnsAsync("No featured offers available. This item cannot be shipped to your selected delivery location. Deliver to Germany");
        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — unavailable, no stray price recorded
        result.Success.Should().BeTrue();
        result.IsOutOfStock.Should().BeTrue();
        result.Price.Should().BeNull("the stray .price cell is an accessory, not a buy-box price");
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.OutOfStock);
    }

    [Fact]
    public async Task ScrapeProductAsync_WhenCancelled_ClosesContextAndThrowsCancellation()
    {
        // Playwright takes timeouts, not tokens, so cancellation aborts by closing the context.
        // The resulting failure must surface as cancellation, NOT as a scrape failure — the handler
        // would otherwise count a host shutdown against the URL's auto-pause budget.
        const string url = "https://example.com/product";
        using var cts = new CancellationTokenSource();

        _browserManagerMock.Setup(x => x.NewPageAsync(It.IsAny<string?>()))
            .ReturnsAsync(_pageMock.Object);
        _contextMock.Setup(x => x.CloseAsync(It.IsAny<BrowserContextCloseOptions>()))
            .Returns(Task.CompletedTask);

        // Simulate the host cancelling mid-navigation: the context close aborts the pending call.
        _pageMock.Setup(x => x.GotoAsync(url, It.IsAny<PageGotoOptions>()))
            .Returns(async () =>
            {
                await cts.CancelAsync();
                throw new PlaywrightException("Target page, context or browser has been closed");
            });

        // Act
        var act = async () => await _service.ScrapeProductAsync(url, cancellationToken: cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _contextMock.Verify(x => x.CloseAsync(It.IsAny<BrowserContextCloseOptions>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ScrapeProductAsync_WhenNotCancelled_StillReportsScrapeFailuresNormally()
    {
        // Guard against over-correcting: a genuine page error must remain a scrape failure result.
        const string url = "https://example.com/product";

        _browserManagerMock.Setup(x => x.NewPageAsync(It.IsAny<string?>()))
            .ReturnsAsync(_pageMock.Object);
        _contextMock.Setup(x => x.CloseAsync(It.IsAny<BrowserContextCloseOptions>()))
            .Returns(Task.CompletedTask);
        _pageMock.Setup(x => x.GotoAsync(url, It.IsAny<PageGotoOptions>()))
            .ThrowsAsync(new PlaywrightException("net::ERR_NAME_NOT_RESOLVED"));

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("ERR_NAME_NOT_RESOLVED");
    }

    private void SetupPage(string url, string? priceText, string? name = null, string? imageUrl = null)
    {
        _browserManagerMock.Setup(x => x.NewPageAsync(It.IsAny<string?>()))
            .ReturnsAsync(_pageMock.Object);

        _pageMock.Setup(x => x.GotoAsync(url, It.IsAny<PageGotoOptions>()))
            .ReturnsAsync((IResponse?)null);

        _pageMock.Setup(x => x.CloseAsync(It.IsAny<PageCloseOptions>()))
            .Returns(Task.CompletedTask);

        // Price extraction iterates all matches per selector; default every selector to no matches.
        _pageMock.Setup(x => x.QuerySelectorAllAsync(It.IsAny<string>()))
            .ReturnsAsync([]);

        // Setup price element
        if (priceText != null)
        {
            var priceElementMock = new Mock<IElementHandle>();
            _pageMock.Setup(x => x.QuerySelectorAllAsync(".price"))
                .ReturnsAsync([priceElementMock.Object]);
            priceElementMock.Setup(x => x.GetAttributeAsync("data-price"))
                .ReturnsAsync((string?)null);
            priceElementMock.Setup(x => x.GetAttributeAsync("content"))
                .ReturnsAsync((string?)null);
            priceElementMock.Setup(x => x.TextContentAsync())
                .ReturnsAsync(priceText);
        }
        else
        {
            _pageMock.Setup(x => x.QuerySelectorAsync(It.IsAny<string>()))
                .ReturnsAsync((IElementHandle?)null);
            _pageMock.Setup(x => x.ContentAsync())
                .ReturnsAsync("<html><body>No price</body></html>");
        }

        // Setup name element
        if (name != null)
        {
            var nameElementMock = new Mock<IElementHandle>();
            _pageMock.Setup(x => x.QuerySelectorAsync("h1"))
                .ReturnsAsync(nameElementMock.Object);
            nameElementMock.Setup(x => x.TextContentAsync())
                .ReturnsAsync(name);
        }

        // Setup image element
        if (imageUrl != null)
        {
            var imageElementMock = new Mock<IElementHandle>();
            _pageMock.Setup(x => x.QuerySelectorAsync("img.product"))
                .ReturnsAsync(imageElementMock.Object);
            imageElementMock.Setup(x => x.GetAttributeAsync("src"))
                .ReturnsAsync(imageUrl);
        }
    }
}
