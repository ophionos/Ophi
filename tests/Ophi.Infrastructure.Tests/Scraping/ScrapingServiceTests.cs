using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;
using RichardSzalay.MockHttp;

namespace Ophi.Infrastructure.Tests.Scraping;

public class ScrapingServiceTests
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly ScrapingService _service;

    public ScrapingServiceTests()
    {
        var loggerMock = new Mock<ILogger<ScrapingService>>();
        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        IStoreConfigProvider configProvider = new CodeStoreConfigProvider();
        _service = new ScrapingService(httpClient, loggerMock.Object, configProvider);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithAmazonPricePattern_ExtractsPrice()
    {
        // Arrange - Use Amazon URL to trigger Amazon adapter
        const string url = "https://www.amazon.com/dp/B123456789";
        const string html = @"
            <html>
                <head><title>Test Product</title></head>
                <body>
                    <h1 id='productTitle'>Test Product Name</h1>
                    <span class='a-price'>
                        <span class='a-offscreen'>$99.99</span>
                    </span>
                    <img id='landingImage' src='https://example.com/image.jpg' />
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Price.Should().Be(99.99m);
        result.Currency.Should().Be("USD");
        result.Name.Should().Be("Test Product Name");
        result.ImageUrl.Should().Be("https://example.com/image.jpg");
        result.StoreId.Should().Be("amazon");
    }

    [Fact]
    public async Task ScrapeProductAsync_SkipsEmptyFirstMatch_AndUsesFirstParseablePrice()
    {
        // Regression for the amazon.es bug: the first `.a-price .a-offscreen` is an empty
        // placeholder, with the real price in a later match and an integer-only `.a-price-whole`
        // ("17.") further down. Using only the first match dropped to `.a-price-whole` -> 17.0 USD.
        // The extractor must skip the empty match and take the first that parses -> 17.91 EUR.
        const string url = "https://www.amazon.es/-/en/dp/1646093240";
        const string html = @"
            <html>
                <head><title>Apothecary Diaries</title></head>
                <body>
                    <h1 id='productTitle'>The Apothecary Diaries</h1>
                    <span class='a-price'><span class='a-offscreen'></span></span>
                    <span class='a-price'><span class='a-offscreen'>€17.91</span></span>
                    <span class='a-price-whole'>17.</span>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — the real EUR price, not 17.0 USD from `.a-price-whole`
        result.Success.Should().BeTrue();
        result.Price.Should().Be(17.91m);
        result.Currency.Should().Be("EUR");
    }

    [Theory]
    [InlineData("This item cannot be shipped to your selected delivery location.", true)]
    [InlineData("THIS ITEM CANNOT BE SHIPPED TO YOUR SELECTED DELIVERY LOCATION", true)] // case-insensitive
    [InlineData("No featured offers available", false)] // not the shipping phrase — contested buybox may have valid offers
    [InlineData("Add to Cart — $99.99 In Stock", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsNoPurchasableOffer_MatchesShippingRestrictionPhrase(string? pageText, bool expected)
    {
        ScrapingService.IsNoPurchasableOffer(pageText, CommonSelectors.UnavailableTextPatterns)
            .Should().Be(expected);
    }

    [Fact]
    public async Task ScrapeProductAsync_WhenItemCannotShipToLocation_RecordsUnavailableWithoutStrayPrice()
    {
        // Regression for the amazon.com geo-block (deployment egresses an EU/PT IP): amazon.com
        // returns "This item cannot be shipped to your selected delivery location" with NO buy-box
        // price. The only price-like text is an unrelated accessory cell whose class contains
        // "price" — which the generic `[class*='price']` fallback would otherwise record as the
        // product's price (the observed `USD 11.16` bug). The no-offer marker must take precedence:
        // discard the stray price and record the listing as unavailable.
        const string url = "https://www.amazon.com/dp/B0CHWRXH8B";
        const string html = @"
            <html>
                <head><title>Apple AirPods Pro</title></head>
                <body>
                    <h1 id='productTitle'>Apple AirPods Pro (2nd Generation)</h1>
                    <div id='buybox'>
                        <span>No featured offers available</span>
                        <span>This item cannot be shipped to your selected delivery location.</span>
                    </div>
                    <table><tr><td>Price</td>
                        <td><span class='a-color-price a-text-bold'>EUR 11.16</span></td>
                    </tr></table>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — unavailable, and crucially NO stray price recorded
        result.Success.Should().BeTrue();
        result.IsOutOfStock.Should().BeTrue();
        result.Price.Should().BeNull("the .a-color-price cell is an unrelated accessory, not a buy-box price");
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.OutOfStock);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithGenericPriceClass_ExtractsPrice()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>$49.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(49.99m);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithDataPriceAttribute_ExtractsPrice()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <span data-price='123.45'>$123.45</span>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(123.45m);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithCustomSelector_UsesCustomSelector()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string customSelector = ".custom-price";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>$999.99</div>
                    <div class='custom-price'>$49.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, customSelector, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(49.99m);
        result.DetectedSelector.Should().Be(customSelector);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithPoundSterling_DetectsGBP()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>£79.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(79.99m);
        result.Currency.Should().Be("GBP");
    }

    [Fact]
    public async Task ScrapeProductAsync_WithEuro_DetectsEUR()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>€59.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(59.99m);
        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task ScrapeProductAsync_WithCommasInPrice_ParsesCorrectly()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>$1,299.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(1299.99m);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithNoPriceFound_ReturnsFailure()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <p>No price available</p>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Could not extract price from page");
    }

    [Fact]
    public async Task ScrapeProductAsync_WithHttpError_ReturnsFailure()
    {
        // Arrange
        const string url = "https://example.com/product";

        _mockHttp.When(url).Respond(System.Net.HttpStatusCode.NotFound);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("HTTP 404");
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.NotFound);
        result.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ScrapeProductAsync_ExtractsProductName()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1 id='productTitle'>  Amazing Product Name  </h1>
                    <div class='price'>$99.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Name.Should().Be("Amazing Product Name");
    }

    [Fact]
    public async Task ScrapeProductAsync_WithLongProductName_TruncatesTo500Characters()
    {
        // Arrange
        const string url = "https://example.com/product";
        var longName = new string('A', 600);
        var html = $@"
            <html>
                <body>
                    <h1 id='productTitle'>{longName}</h1>
                    <div class='price'>$99.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Name.Should().HaveLength(500);
    }

    [Fact]
    public async Task ScrapeProductAsync_ExtractsImageUrl()
    {
        // Arrange - Use og:image meta tag (generic selector)
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <head>
                    <meta property='og:image' content='https://example.com/product.jpg' />
                </head>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>$99.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.ImageUrl.Should().Be("https://example.com/product.jpg");
    }

    [Fact]
    public async Task ScrapeProductAsync_WithRelativeImageUrl_ConvertsToAbsolute()
    {
        // Arrange - Use og:image meta tag with relative URL
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <head>
                    <meta property='og:image' content='/images/product.jpg' />
                </head>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>$99.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.ImageUrl.Should().Be("https://example.com/images/product.jpg");
    }

    [Fact]
    public async Task ScrapeProductAsync_WithProtocolRelativeUrl_AddsHttps()
    {
        // Arrange - Use og:image meta tag with protocol-relative URL
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <head>
                    <meta property='og:image' content='//cdn.example.com/image.jpg' />
                </head>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>$99.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.ImageUrl.Should().Be("https://cdn.example.com/image.jpg");
    }

    [Fact]
    public async Task ScrapeProductAsync_WithPriceWithoutDecimals_ParsesCorrectly()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>$99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(99m);
    }

    #region Price Parsing Edge Cases

    [Fact]
    public async Task ScrapeProductAsync_WithThousandsSeparator_ParsesCorrectly()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>$1,299.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(1299.99m);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithYen_DetectsJPY()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>¥1500</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(1500m);
        result.Currency.Should().Be("JPY");
    }

    [Fact]
    public async Task ScrapeProductAsync_WithWhitespaceInPrice_ParsesCorrectly()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>$ 29.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(29.99m);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithPriceRange_TakesFirstPrice()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>$29.99 - $39.99</div>
                </body>
            </html>";

        _mockHttp.When(url)
            .Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(29.99m);
    }

    #endregion

    #region Selector Extraction

    [Fact]
    public async Task ScrapeProductAsync_ExtractsPrice_FromDataPriceAttribute()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <span class='price' data-price='45.50'>Display: $45.50</span>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(45.50m);
    }

    [Fact]
    public async Task ScrapeProductAsync_ExtractsPrice_FromContentAttribute()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <meta itemprop='price' content='67.89' />
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(67.89m);
    }

    #endregion

    #region Extraction Pipeline

    [Fact]
    public async Task ScrapeProductAsync_FallsBackToGeneric_WhenStoreSpecificFails()
    {
        // Arrange - Use Amazon URL but provide HTML that doesn't match Amazon selectors
        const string url = "https://www.amazon.com/dp/B123456789";
        const string html = @"
            <html>
                <body>
                    <h1 class='title'>Generic Product Title</h1>
                    <div class='price'>$19.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Should still find price via generic selectors
        result.Success.Should().BeTrue();
        result.Price.Should().Be(19.99m);
        result.StoreId.Should().Be("amazon"); // Still detected as Amazon URL
    }

    #endregion

    #region HTTP Errors

    [Theory]
    [InlineData(System.Net.HttpStatusCode.Forbidden, ScrapeErrorCategory.Forbidden)]
    [InlineData(System.Net.HttpStatusCode.InternalServerError, ScrapeErrorCategory.ServerError)]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable, ScrapeErrorCategory.ServerError)]
    public async Task ScrapeProductAsync_ReturnsFailure_OnNonSuccessStatus(System.Net.HttpStatusCode statusCode, ScrapeErrorCategory expectedCategory)
    {
        // Arrange
        const string url = "https://example.com/product";

        _mockHttp.When(url).Respond(statusCode);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("HTTP");
        result.ErrorCategory.Should().Be(expectedCategory);
        result.HttpStatusCode.Should().Be((int)statusCode);
    }

    [Fact]
    public async Task ScrapeProductAsync_ReturnsFailure_OnNetworkError()
    {
        // Arrange
        const string url = "https://example.com/product";

        _mockHttp.When(url).Throw(new HttpRequestException("Connection refused"));

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Network error");
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.NetworkError);
    }

    #endregion

    #region Currency Extraction

    [Fact]
    public async Task ScrapeProductAsync_ExtractsCurrency_FromMetaTag_OverridesSymbol()
    {
        // Arrange - Meta tag says CAD but symbol looks like USD
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <head>
                    <meta property='product:price:currency' content='CAD' />
                </head>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>$99.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Price.Should().Be(99.99m);
        result.Currency.Should().Be("CAD"); // Meta tag overrides symbol detection
    }

    [Fact]
    public async Task ScrapeProductAsync_ExtractsCurrency_FromOgMetaTag()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <head>
                    <meta property='og:price:currency' content='EUR' />
                </head>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>$49.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task ScrapeProductAsync_ExtractsCurrency_FromItemPropMeta()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <span itemprop='priceCurrency' content='GBP'>GBP</span>
                    <div class='price'>£29.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Currency.Should().Be("GBP");
    }

    #endregion

    #region User Config Resolution

    [Fact]
    public async Task ScrapeProductAsync_WithUserId_CallsGetConfigForUrlAsync()
    {
        // Arrange
        const string url = "https://example.com/product";
        var userId = Guid.NewGuid();
        var mockHttp = new MockHttpMessageHandler();
        const string html = @"
            <html><body>
                <h1>Test Product</h1>
                <div class='price'>499,90 €</div>
            </body></html>";
        mockHttp.When(url).Respond("text/html", html);

        var configProviderMock = new Mock<IStoreConfigProvider>();
        configProviderMock.Setup(x => x.GetConfigForUrlAsync(url, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoreConfig
            {
                Id = "user-store",
                Name = "User Store",
                PriceLocale = "pt-PT",
                DomainPatterns = ["example.com"],
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = [".price"],
                    NameSelectors = ["h1"],
                    ImageSelectors = ["img"]
                }
            });
        configProviderMock.Setup(x => x.GetGenericConfig())
            .Returns(new CodeStoreConfigProvider().GetGenericConfig());

        var loggerMock = new Mock<ILogger<ScrapingService>>();
        var service = new ScrapingService(mockHttp.ToHttpClient(), loggerMock.Object, configProviderMock.Object);

        // Act
        await service.ScrapeProductAsync(url, userId: userId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — GetConfigForUrlAsync was called on the interface (not a downcast)
        configProviderMock.Verify(x => x.GetConfigForUrlAsync(url, userId, It.IsAny<CancellationToken>()), Times.Once);
        configProviderMock.Verify(x => x.GetConfigForUrl(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithUserId_UsesPriceLocaleFromUserConfig()
    {
        // Arrange — pt-PT locale uses comma as decimal separator
        const string url = "https://example.com/product";
        var userId = Guid.NewGuid();
        var mockHttp = new MockHttpMessageHandler();
        const string html = @"
            <html><body>
                <h1>Test Product</h1>
                <div class='price'>499,90 €</div>
            </body></html>";
        mockHttp.When(url).Respond("text/html", html);

        var configProviderMock = new Mock<IStoreConfigProvider>();
        configProviderMock.Setup(x => x.GetConfigForUrlAsync(url, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoreConfig
            {
                Id = "user-store",
                Name = "User Store",
                PriceLocale = "pt-PT",
                DomainPatterns = ["example.com"],
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = [".price"],
                    NameSelectors = ["h1"],
                    ImageSelectors = ["img"]
                }
            });
        configProviderMock.Setup(x => x.GetGenericConfig())
            .Returns(new CodeStoreConfigProvider().GetGenericConfig());

        var loggerMock = new Mock<ILogger<ScrapingService>>();
        var service = new ScrapingService(mockHttp.ToHttpClient(), loggerMock.Object, configProviderMock.Object);

        // Act
        var result = await service.ScrapeProductAsync(url, userId: userId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — 499,90 should be parsed as 499.90 with pt-PT locale, not 49990
        result.Success.Should().BeTrue();
        result.Price.Should().Be(499.90m);
        result.StoreId.Should().Be("user-store");
    }

    [Fact]
    public async Task ScrapeProductAsync_WithoutUserId_CallsGetConfigForUrl()
    {
        // Arrange
        const string url = "https://example.com/product";
        var mockHttp = new MockHttpMessageHandler();
        const string html = @"
            <html><body>
                <h1>Test Product</h1>
                <div class='price'>$49.99</div>
            </body></html>";
        mockHttp.When(url).Respond("text/html", html);

        var configProviderMock = new Mock<IStoreConfigProvider>();
        configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);
        configProviderMock.Setup(x => x.GetGenericConfig())
            .Returns(new CodeStoreConfigProvider().GetGenericConfig());

        var loggerMock = new Mock<ILogger<ScrapingService>>();
        var service = new ScrapingService(mockHttp.ToHttpClient(), loggerMock.Object, configProviderMock.Object);

        // Act
        await service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — sync method used when no userId
        configProviderMock.Verify(x => x.GetConfigForUrl(url), Times.Once);
        configProviderMock.Verify(x => x.GetConfigForUrlAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Store ID Detection

    [Theory]
    [InlineData("https://www.amazon.com/dp/B123", "amazon")]
    [InlineData("https://www.ebay.com/itm/123", "ebay")]
    [InlineData("https://www.unknownstore.com/product", "generic")]
    public async Task ScrapeProductAsync_ReturnsCorrectStoreId(string url, string expectedStoreId)
    {
        // Arrange
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>$99.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.StoreId.Should().Be(expectedStoreId);
    }

    #endregion

    #region Out-of-Stock Detection

    [Fact]
    public async Task ScrapeProductAsync_WithJsonLdOutOfStock_ReturnsOutOfStock()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <head>
                    <script type='application/ld+json'>
                    {
                        ""@type"": ""Product"",
                        ""name"": ""Test Product"",
                        ""offers"": {
                            ""@type"": ""Offer"",
                            ""availability"": ""https://schema.org/OutOfStock""
                        }
                    }
                    </script>
                </head>
                <body>
                    <h1>Test Product</h1>
                    <p>This product is currently unavailable.</p>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.IsOutOfStock.Should().BeTrue();
        result.Price.Should().BeNull();
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.OutOfStock);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithOutOfStockCssPattern_ReturnsOutOfStock()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <div class='out-of-stock'>Out of Stock</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.IsOutOfStock.Should().BeTrue();
        result.Price.Should().BeNull();
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.OutOfStock);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithOutOfStockAndPrice_ReturnsBoth()
    {
        // Arrange - Page shows price alongside out-of-stock indicator
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <head>
                    <script type='application/ld+json'>
                    {
                        ""@type"": ""Product"",
                        ""offers"": {
                            ""@type"": ""Offer"",
                            ""availability"": ""https://schema.org/OutOfStock""
                        }
                    }
                    </script>
                </head>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>$49.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.IsOutOfStock.Should().BeTrue();
        result.Price.Should().Be(49.99m);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithInStockSchemaOrg_ReturnsInStock()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <head>
                    <script type='application/ld+json'>
                    {
                        ""@type"": ""Product"",
                        ""offers"": {
                            ""@type"": ""Offer"",
                            ""availability"": ""https://schema.org/InStock""
                        }
                    }
                    </script>
                </head>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>$29.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.IsOutOfStock.Should().BeFalse();
        result.Price.Should().Be(29.99m);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithNoPriceNoOosIndicators_ReturnsParseError()
    {
        // Arrange - Page has no price and no out-of-stock indicators
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <body>
                    <h1>Test Product</h1>
                    <p>Some description text without any price or stock info.</p>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.ParseError);
    }

    [Fact]
    public async Task ScrapeProductAsync_With429_ReturnsRateLimited()
    {
        // Arrange
        const string url = "https://example.com/product";

        _mockHttp.When(url).Respond(System.Net.HttpStatusCode.TooManyRequests);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.RateLimited);
        result.HttpStatusCode.Should().Be(429);
    }

    [Fact]
    public async Task ScrapeProductAsync_With410_ReturnsNotFound()
    {
        // Arrange
        const string url = "https://example.com/product";

        _mockHttp.When(url).Respond(System.Net.HttpStatusCode.Gone);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCategory.Should().Be(ScrapeErrorCategory.NotFound);
        result.HttpStatusCode.Should().Be(410);
    }

    #endregion

    #region PageTitle

    [Fact]
    public async Task ScrapeProductAsync_PopulatesPageTitle()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string html = @"
            <html>
                <head><title>My Product</title></head>
                <body>
                    <h1>Test Product</h1>
                    <div class='price'>$59.99</div>
                </body>
            </html>";

        _mockHttp.When(url).Respond("text/html", html);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.PageTitle.Should().Be("My Product");
    }

    #endregion

    #region JSONPath Extraction

    [Fact]
    public async Task ScrapeWithConfigAsync_WithJsonPathPrice_ExtractsViaJsonPath()
    {
        const string url = "https://example.com/product";
        const string html = """
            <html>
                <head>
                    <script type="application/ld+json">
                    {"@type":"Product","name":"Widget","offers":{"price":"42.99","priceCurrency":"USD"}}
                    </script>
                </head>
                <body><h1>Widget</h1></body>
            </html>
            """;

        _mockHttp.When(url).Respond("text/html", html);

        var config = new StoreConfig
        {
            Id = "test-jsonpath",
            Name = "Test JSONPath Store",
            DomainPatterns = ["example.com"],
            Selectors = new StoreSelectorConfig
            {
                PriceSelectors = [".nonexistent-price"],
                NameSelectors = ["h1"],
                ImageSelectors = ["img"],
                PriceJsonPaths = ["$.offers.price"]
            }
        };

        var result = await _service.ScrapeWithConfigAsync(url, config, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Price.Should().Be(42.99m);
        result.DetectedSelector.Should().Be("jsonpath:$.offers.price");
    }

    [Fact]
    public async Task ScrapeWithConfigAsync_WithJsonPathName_ExtractsName()
    {
        const string url = "https://example.com/product";
        const string html = """
            <html>
                <head>
                    <script type="application/ld+json">
                    {"@type":"Product","name":"Sony WH-1000XM5","offers":{"price":"299.99"}}
                    </script>
                </head>
                <body><div class="price">$299.99</div></body>
            </html>
            """;

        _mockHttp.When(url).Respond("text/html", html);

        var config = new StoreConfig
        {
            Id = "test-jsonpath",
            Name = "Test",
            DomainPatterns = ["example.com"],
            Selectors = new StoreSelectorConfig
            {
                PriceSelectors = [".price"],
                NameSelectors = [".nonexistent-name"],
                ImageSelectors = ["img"],
                NameJsonPaths = ["$.name"]
            }
        };

        var result = await _service.ScrapeWithConfigAsync(url, config, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Name.Should().Be("Sony WH-1000XM5");
    }

    [Fact]
    public async Task ScrapeWithConfigAsync_WithJsonPathImage_ExtractsImage()
    {
        const string url = "https://example.com/product";
        const string html = """
            <html>
                <head>
                    <script type="application/ld+json">
                    {"@type":"Product","name":"Widget","image":"https://cdn.example.com/img.jpg","offers":{"price":"9.99"}}
                    </script>
                </head>
                <body><div class="price">$9.99</div></body>
            </html>
            """;

        _mockHttp.When(url).Respond("text/html", html);

        var config = new StoreConfig
        {
            Id = "test-jsonpath",
            Name = "Test",
            DomainPatterns = ["example.com"],
            Selectors = new StoreSelectorConfig
            {
                PriceSelectors = [".price"],
                NameSelectors = ["h1"],
                ImageSelectors = [".nonexistent-img"],
                ImageJsonPaths = ["$.image"]
            }
        };

        var result = await _service.ScrapeWithConfigAsync(url, config, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.ImageUrl.Should().Be("https://cdn.example.com/img.jpg");
    }

    [Fact]
    public async Task ScrapeWithConfigAsync_CssTakesPriority_OverJsonPath()
    {
        const string url = "https://example.com/product";
        const string html = """
            <html>
                <head>
                    <script type="application/ld+json">
                    {"@type":"Product","offers":{"price":"50.00"}}
                    </script>
                </head>
                <body><div class="price">$42.99</div></body>
            </html>
            """;

        _mockHttp.When(url).Respond("text/html", html);

        var config = new StoreConfig
        {
            Id = "test-jsonpath",
            Name = "Test",
            DomainPatterns = ["example.com"],
            Selectors = new StoreSelectorConfig
            {
                PriceSelectors = [".price"],
                NameSelectors = ["h1"],
                ImageSelectors = ["img"],
                PriceJsonPaths = ["$.offers.price"]
            }
        };

        var result = await _service.ScrapeWithConfigAsync(url, config, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Price.Should().Be(42.99m, "CSS selectors should take priority over JSONPath");
        result.DetectedSelector.Should().Be(".price");
    }

    [Fact]
    public async Task ScrapeWithConfigAsync_JsonPathFallsBackToRegex_WhenJsonPathFails()
    {
        const string url = "https://example.com/product";
        const string html = """
            <html>
                <head>
                    <script type="application/ld+json">
                    {"@type":"Product","name":"Widget"}
                    </script>
                </head>
                <body>
                    <span data-price-value="19.99">$19.99</span>
                </body>
            </html>
            """;

        _mockHttp.When(url).Respond("text/html", html);

        var config = new StoreConfig
        {
            Id = "test-jsonpath",
            Name = "Test",
            DomainPatterns = ["example.com"],
            Selectors = new StoreSelectorConfig
            {
                PriceSelectors = [".nonexistent"],
                NameSelectors = ["h1"],
                ImageSelectors = ["img"],
                PriceJsonPaths = ["$.offers.price"],
                PriceRegexPatterns = ["""data-price-value="(\d+\.\d+)"""]
            }
        };

        var result = await _service.ScrapeWithConfigAsync(url, config, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Price.Should().Be(19.99m, "should fall back to regex when JSONPath finds no match");
        result.DetectedSelector.Should().StartWith("regex:");
    }

    #endregion

    #region Custom User-Agent

    [Fact]
    public async Task ScrapeWithConfigAsync_WithCustomUserAgent_SendsConfiguredUA()
    {
        const string url = "https://example.com/product";
        const string customUa = "Mozilla/5.0 CustomTestBrowser/1.0";
        const string html = "<html><body><div class='price'>$10.00</div></body></html>";

        string? capturedUa = null;
        _mockHttp.When(url)
            .Respond(req =>
            {
                capturedUa = req.Headers.UserAgent.ToString();
                return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent(html, System.Text.Encoding.UTF8, "text/html")
                });
            });

        var config = new StoreConfig
        {
            Id = "custom",
            Name = "Custom Store",
            DomainPatterns = ["example.com"],
            Selectors = new StoreSelectorConfig
            {
                PriceSelectors = [".price"],
                NameSelectors = ["h1"],
                ImageSelectors = ["img"]
            },
            CustomUserAgent = customUa
        };

        await _service.ScrapeWithConfigAsync(url, config, TestContext.Current.CancellationToken);

        capturedUa.Should().Contain("CustomTestBrowser/1.0");
    }

    [Fact]
    public async Task ScrapeWithConfigAsync_WithoutCustomUserAgent_SendsProfileUA()
    {
        const string url = "https://example.com/product";
        const string html = "<html><body><div class='price'>$10.00</div></body></html>";

        string? capturedUa = null;
        _mockHttp.When(url)
            .Respond(req =>
            {
                capturedUa = req.Headers.UserAgent.ToString();
                return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent(html, System.Text.Encoding.UTF8, "text/html")
                });
            });

        var config = new StoreConfig
        {
            Id = "custom",
            Name = "Custom Store",
            DomainPatterns = ["example.com"],
            Selectors = new StoreSelectorConfig
            {
                PriceSelectors = [".price"],
                NameSelectors = ["h1"],
                ImageSelectors = ["img"]
            }
            // No CustomUserAgent
        };

        await _service.ScrapeWithConfigAsync(url, config, TestContext.Current.CancellationToken);

        // Should use one of the browser profiles — all contain "Mozilla/5.0"
        capturedUa.Should().Contain("Mozilla/5.0");
    }

    #endregion
}
