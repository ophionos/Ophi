using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Ophi.Infrastructure.Scraping;

namespace Ophi.Infrastructure.Tests.Scraping;

public class AutoCreateStoreServiceTests
{
    private readonly AutoCreateStoreService _service;

    public AutoCreateStoreServiceTests()
    {
        var logger = new Mock<ILogger<AutoCreateStoreService>>();
        _service = new AutoCreateStoreService(logger.Object);
    }

    // --- OpenGraph strategy ---

    [Fact]
    public async Task AnalyzeHtmlAsync_WithOpenGraphPriceMeta_DetectsPriceSelector()
    {
        const string html = """
            <html><head>
                <meta property="product:price:amount" content="29.99" />
                <meta property="og:title" content="Cool Widget" />
                <meta property="og:image" content="https://example.com/image.jpg" />
            </head><body><h1>Cool Widget</h1></body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://shop.example.com/product/1", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Selectors.PriceSelectors.Should().Contain("meta[property='product:price:amount']|content");
        result.Selectors.NameSelectors.Should().Contain("meta[property='og:title']|content");
        result.Selectors.ImageSelectors.Should().Contain("meta[property='og:image']|content");
    }

    [Fact]
    public async Task AnalyzeHtmlAsync_WithOgPriceAmount_DetectsPriceSelector()
    {
        const string html = """
            <html><head>
                <meta property="og:price:amount" content="49.99" />
            </head><body><h1>Product</h1><span class="price">$49.99</span></body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://example.com/p/1", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Selectors.PriceSelectors.Should().Contain("meta[property='og:price:amount']|content");
    }

    // --- JSON-LD strategy ---

    [Fact]
    public async Task AnalyzeHtmlAsync_WithJsonLdProduct_DetectsItempropSelectors()
    {
        const string html = """
            <html><head>
                <script type="application/ld+json">
                {"@type": "Product", "name": "Widget", "offers": {"price": "19.99"}}
                </script>
            </head><body>
                <h1>Widget</h1>
                <span itemprop="price" content="19.99">$19.99</span>
            </body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://widgets.com/product/42", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Selectors.PriceSelectors.Should().Contain("[itemprop='price']|content");
        result.Selectors.PriceSelectors.Should().Contain("[itemprop='price']");
    }

    [Fact]
    public async Task AnalyzeHtmlAsync_WithJsonLdArray_DetectsProduct()
    {
        const string html = """
            <html><head>
                <script type="application/ld+json">
                [{"@type": "Organization"}, {"@type": "Product", "name": "Gadget"}]
                </script>
            </head><body>
                <h1>Gadget</h1>
                <span itemprop="price" content="9.99">$9.99</span>
            </body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://gadgets.com/item/1", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Selectors.PriceSelectors.Should().Contain("[itemprop='price']|content");
    }

    // --- CSS patterns strategy ---

    [Fact]
    public async Task AnalyzeHtmlAsync_WithPriceClass_DetectsCssSelector()
    {
        const string html = """
            <html><body>
                <h1>Simple Product</h1>
                <span class="price">$15.00</span>
            </body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://simple-store.com/p/1", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Selectors.PriceSelectors.Should().Contain(".price");
    }

    [Fact]
    public async Task AnalyzeHtmlAsync_WithDataPrice_DetectsSelector()
    {
        const string html = """
            <html><body>
                <h1>Data Product</h1>
                <div data-price="25.50">$25.50</div>
            </body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://store.com/item", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Selectors.PriceSelectors.Should().Contain("[data-price]");
    }

    [Fact]
    public async Task AnalyzeHtmlAsync_WithItempropPrice_DetectsCssSelector()
    {
        const string html = """
            <html><body>
                <h1>Itemprop Product</h1>
                <span itemprop="price" content="35.00">$35.00</span>
            </body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://store.com/item", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Selectors.PriceSelectors.Should().Contain("[itemprop='price']|content");
    }

    // --- Regex fallback strategy ---

    [Fact]
    public async Task AnalyzeHtmlAsync_WithPriceInJson_DetectsRegexPattern()
    {
        const string html = """
            <html><body>
                <h1>Product</h1>
                <span class="price">$10.00</span>
                <script>var data = {"price": "10.00"};</script>
            </body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://store.com/item", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Selectors.PriceRegexPatterns.Should().NotBeNull();
        result.Selectors.PriceRegexPatterns.Should().Contain(@"""price""\s?:\s?""([^""]+)""");
    }

    [Fact]
    public async Task AnalyzeHtmlAsync_WithPriceInHtmlTags_DetectsRegexPattern()
    {
        const string html = """
            <html><body>
                <h1>Product</h1>
                <span class="price">$42.99</span>
                <div>Regular price: <b>$42.99</b></div>
            </body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://store.com/item", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Selectors.PriceRegexPatterns.Should().NotBeNull();
        result.Selectors.PriceRegexPatterns.Should().Contain(@">\$(\d+(?:\.\d{2})?)<");
    }

    // --- Store name and domain extraction ---

    [Fact]
    public async Task AnalyzeHtmlAsync_WithOgSiteName_UsesItAsStoreName()
    {
        const string html = """
            <html><head>
                <meta property="og:site_name" content="My Awesome Shop" />
                <meta property="product:price:amount" content="10.00" />
            </head><body><h1>Product</h1></body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://www.myshop.com/product/1", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.StoreName.Should().Be("My Awesome Shop");
        result.Domain.Should().Be("myshop.com");
    }

    [Fact]
    public async Task AnalyzeHtmlAsync_WithoutOgSiteName_CapitalizesDomain()
    {
        const string html = """
            <html><body>
                <h1>Product</h1>
                <span class="price">$10.00</span>
            </body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://coolshop.com/item", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.StoreName.Should().Be("Coolshop");
    }

    [Fact]
    public async Task AnalyzeHtmlAsync_WithWwwPrefix_StripsDomain()
    {
        const string html = """
            <html><body>
                <h1>Product</h1>
                <span class="price">$10.00</span>
            </body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://www.example.com/p", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Domain.Should().Be("example.com");
    }

    // --- Name selectors ---

    [Fact]
    public async Task AnalyzeHtmlAsync_WithH1Element_DetectsNameSelector()
    {
        const string html = """
            <html><body>
                <h1>Great Product Name</h1>
                <span class="price">$10.00</span>
            </body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://store.com/item", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Selectors.NameSelectors.Should().Contain("h1");
    }

    [Fact]
    public async Task AnalyzeHtmlAsync_WithItempropName_DetectsNameSelector()
    {
        const string html = """
            <html><body>
                <h1 itemprop="name">Named Product</h1>
                <span class="price">$10.00</span>
            </body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://store.com/item", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Selectors.NameSelectors.Should().Contain("[itemprop='name']");
    }

    // --- Image selectors ---

    [Fact]
    public async Task AnalyzeHtmlAsync_WithOgImage_DetectsImageSelector()
    {
        const string html = """
            <html><head>
                <meta property="og:image" content="https://store.com/img.jpg" />
                <meta property="product:price:amount" content="10.00" />
            </head><body><h1>Product</h1></body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://store.com/item", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Selectors.ImageSelectors.Should().Contain("meta[property='og:image']|content");
    }

    [Fact]
    public async Task AnalyzeHtmlAsync_WithItempropImage_DetectsImageSelector()
    {
        const string html = """
            <html><body>
                <h1>Product</h1>
                <span class="price">$10.00</span>
                <img itemprop="image" src="https://store.com/img.jpg" />
            </body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://store.com/item", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Selectors.ImageSelectors.Should().Contain("[itemprop='image']");
    }

    // --- Edge cases ---

    [Fact]
    public async Task AnalyzeHtmlAsync_WithNoPriceElements_ReturnsNull()
    {
        const string html = """
            <html><body>
                <h1>About Us</h1>
                <p>We are a great company.</p>
            </body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://store.com/about", TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AnalyzeHtmlAsync_WithEmptyHtml_ReturnsNull()
    {
        var result = await _service.AnalyzeHtmlAsync("", "https://store.com/item", TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AnalyzeHtmlAsync_WithInvalidUrl_ReturnsNull()
    {
        var result = await _service.AnalyzeHtmlAsync("<html></html>", "not-a-url", TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AnalyzeHtmlAsync_WithMalformedJsonLd_SkipsWithoutError()
    {
        const string html = """
            <html><head>
                <script type="application/ld+json">{invalid json}</script>
            </head><body>
                <h1>Product</h1>
                <span class="price">$10.00</span>
            </body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://store.com/item", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Selectors.PriceSelectors.Should().Contain(".price");
    }

    // --- Multiple strategies combined ---

    [Fact]
    public async Task AnalyzeHtmlAsync_WithMultipleStrategies_CombinesSelectors()
    {
        const string html = """
            <html><head>
                <meta property="product:price:amount" content="29.99" />
                <meta property="og:title" content="Super Widget" />
                <meta property="og:image" content="https://store.com/img.jpg" />
                <script type="application/ld+json">{"@type":"Product","name":"Super Widget"}</script>
            </head><body>
                <h1 itemprop="name">Super Widget</h1>
                <span itemprop="price" content="29.99">$29.99</span>
                <span class="price">$29.99</span>
            </body></html>
            """;

        var result = await _service.AnalyzeHtmlAsync(html, "https://store.com/item", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        // Should have OG, JSON-LD, and CSS selectors
        result.Selectors.PriceSelectors.Should().Contain("meta[property='product:price:amount']|content");
        result.Selectors.PriceSelectors.Should().Contain("[itemprop='price']|content");
        result.Selectors.PriceSelectors.Should().Contain(".price");
        result.Selectors.PriceSelectors.Count().Should().BeGreaterThan(2);
    }
}
