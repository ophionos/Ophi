using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Infrastructure.Tests.Scraping;

public class HybridScrapingServiceTests
{
    private readonly Mock<IScrapingService> _httpServiceMock;
    private readonly Mock<IScrapingService> _playwrightServiceMock;
    private readonly Mock<IStoreConfigProvider> _configProviderMock;
    private readonly HybridScrapingService _service;

    public HybridScrapingServiceTests()
    {
        _httpServiceMock = new Mock<IScrapingService>();
        _playwrightServiceMock = new Mock<IScrapingService>();
        _configProviderMock = new Mock<IStoreConfigProvider>();
        var loggerMock = new Mock<ILogger<HybridScrapingService>>();

        _service = new HybridScrapingService(
            _httpServiceMock.Object,
            _playwrightServiceMock.Object,
            _configProviderMock.Object,
            loggerMock.Object);
    }

    [Fact]
    public async Task UsesPlaywrightDirectly_WhenRequiresJavaScriptTrue()
    {
        // Arrange
        const string url = "https://example.com/product";
        var expectedResult = new ScrapingResult
        {
            Success = true,
            Name = "Test Product",
            Price = 99.99m,
            Currency = "USD"
        };

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns(new StoreConfig
            {
                Id = "custom-store",
                Name = "Custom Store",
                RequiresJavaScript = true,
                DomainPatterns = ["example.com"],
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = [".price"],
                    NameSelectors = ["h1"],
                    ImageSelectors = ["img"]
                }
            });

        _playwrightServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(expectedResult);
        _playwrightServiceMock.Verify(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        _httpServiceMock.Verify(x => x.ScrapeProductAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UsesPlaywrightDirectly_ForAmazonDomains()
    {
        // Arrange
        const string url = "https://www.amazon.com/dp/B123456789";
        var expectedResult = new ScrapingResult
        {
            Success = true,
            Name = "Amazon Product",
            Price = 49.99m,
            Currency = "USD"
        };

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns(new StoreConfig
            {
                Id = "amazon",
                Name = "Amazon",
                RequiresJavaScript = true,
                DomainPatterns = ["amazon.com"],
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = [".a-price"],
                    NameSelectors = ["#productTitle"],
                    ImageSelectors = ["#landingImage"]
                }
            });

        _playwrightServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(expectedResult);
        _playwrightServiceMock.Verify(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        _httpServiceMock.Verify(x => x.ScrapeProductAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("https://www.amazon.com/dp/B123")]
    [InlineData("https://www.amazon.co.uk/dp/B456")]
    [InlineData("https://smile.amazon.com/dp/B789")]
    public async Task UsesPlaywrightDirectly_ForAmazonVariants(string url)
    {
        // Arrange
        var expectedResult = new ScrapingResult
        {
            Success = true,
            Name = "Amazon Product",
            Price = 29.99m,
            Currency = "USD"
        };

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns(new StoreConfig
            {
                Id = "amazon",
                Name = "Amazon",
                RequiresJavaScript = true,
                DomainPatterns = ["amazon.com", "amazon.co.uk"],
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = [".a-price"],
                    NameSelectors = ["#productTitle"],
                    ImageSelectors = ["#landingImage"]
                }
            });

        _playwrightServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(expectedResult);
        _playwrightServiceMock.Verify(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UsesHttpFirst_ForNonJsStores()
    {
        // Arrange
        const string url = "https://www.ebay.com/itm/123456";
        var expectedResult = new ScrapingResult
        {
            Success = true,
            Name = "eBay Product",
            Price = 19.99m,
            Currency = "USD"
        };

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns(new StoreConfig
            {
                Id = "ebay",
                Name = "eBay",
                RequiresJavaScript = false,
                DomainPatterns = ["ebay.com"],
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = [".price"],
                    NameSelectors = ["h1"],
                    ImageSelectors = ["img"]
                }
            });

        _httpServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(expectedResult);
        _httpServiceMock.Verify(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        _playwrightServiceMock.Verify(x => x.ScrapeProductAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FallsBackToPlaywright_WhenHttpFails()
    {
        // Arrange
        const string url = "https://example.com/product";
        var httpResult = ScrapingResult.Failure("HTTP scraping failed");
        var playwrightResult = new ScrapingResult
        {
            Success = true,
            Name = "Test Product",
            Price = 49.99m,
            Currency = "USD"
        };

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        _httpServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(httpResult);
        _playwrightServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(playwrightResult);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(playwrightResult);
        _httpServiceMock.Verify(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        _playwrightServiceMock.Verify(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FallsBackToPlaywright_WhenUnknownProduct()
    {
        // Arrange
        const string url = "https://example.com/product";
        var httpResult = new ScrapingResult
        {
            Success = true,
            Name = "Unknown Product",
            Price = 0,
            Currency = "USD"
        };
        var playwrightResult = new ScrapingResult
        {
            Success = true,
            Name = "Real Product Name",
            Price = 29.99m,
            Currency = "USD"
        };

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        _httpServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(httpResult);
        _playwrightServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(playwrightResult);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(playwrightResult);
    }

    [Fact]
    public async Task FallsBackToPlaywright_WhenPriceNull()
    {
        // Arrange — HTTP returns null Price (no price found), should fall back to Playwright
        const string url = "https://example.com/product";
        var httpResult = new ScrapingResult
        {
            Success = true,
            Name = "Test Product",
            Price = null,
            Currency = "USD"
        };
        var playwrightResult = new ScrapingResult
        {
            Success = true,
            Name = "Test Product",
            Price = 19.99m,
            Currency = "USD"
        };

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        _httpServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(httpResult);
        _playwrightServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(playwrightResult);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(playwrightResult);
    }

    [Fact]
    public async Task DoesNotFallBackToPlaywright_WhenPriceIsExplicitlyZero()
    {
        // Arrange — Price = 0 is a valid free-product price, not a sentinel for "missing"
        const string url = "https://example.com/product";
        var httpResult = new ScrapingResult
        {
            Success = true,
            Name = "Free Product",
            Price = 0m,
            Currency = "USD"
        };

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        _httpServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(httpResult);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — Playwright should NOT be called for a valid zero price
        result.Price.Should().Be(0m);
        _playwrightServiceMock.Verify(
            x => x.ScrapeProductAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReturnsHttpResult_WhenComplete()
    {
        // Arrange
        const string url = "https://example.com/product";
        var expectedResult = new ScrapingResult
        {
            Success = true,
            Name = "Complete Product",
            Price = 59.99m,
            Currency = "USD",
            ImageUrl = "https://example.com/image.jpg"
        };

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        _httpServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(expectedResult);
        _playwrightServiceMock.Verify(x => x.ScrapeProductAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReturnsPlaywrightResult_AfterFallback()
    {
        // Arrange
        const string url = "https://example.com/product";
        var httpResult = ScrapingResult.Failure("Failed");
        var playwrightResult = new ScrapingResult
        {
            Success = true,
            Name = "Playwright Product",
            Price = 39.99m,
            Currency = "EUR"
        };

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        _httpServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(httpResult);
        _playwrightServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(playwrightResult);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Name.Should().Be("Playwright Product");
        result.Price.Should().Be(39.99m);
        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task ReturnsFailure_WhenBothFail()
    {
        // Arrange
        const string url = "https://example.com/product";
        var httpResult = ScrapingResult.Failure("HTTP failed");
        var playwrightResult = ScrapingResult.Failure("Playwright failed");

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        _httpServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(httpResult);
        _playwrightServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(playwrightResult);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - returns HTTP result when both fail and Playwright doesn't improve
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task PassesCustomSelector_ToUnderlyingService()
    {
        // Arrange
        const string url = "https://example.com/product";
        const string customSelector = ".custom-price";
        var expectedResult = new ScrapingResult
        {
            Success = true,
            Name = "Product",
            Price = 15.00m,
            Currency = "USD",
            DetectedSelector = customSelector
        };

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        _httpServiceMock.Setup(x => x.ScrapeProductAsync(url, customSelector, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.ScrapeProductAsync(url, customSelector, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(expectedResult);
        _httpServiceMock.Verify(x => x.ScrapeProductAsync(url, customSelector, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PassesCancellationToken_ToUnderlyingService()
    {
        // Arrange
        const string url = "https://example.com/product";
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        var expectedResult = new ScrapingResult
        {
            Success = true,
            Name = "Product",
            Price = 25.00m,
            Currency = "USD"
        };

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        _httpServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), token))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.ScrapeProductAsync(url, null, null, cancellationToken: token);

        // Assert
        result.Should().Be(expectedResult);
        _httpServiceMock.Verify(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), token), Times.Once);
    }

    [Fact]
    public async Task ScrapeProductAsync_WithUserId_CallsGetConfigForUrlAsyncOnInterface()
    {
        // Arrange — verifies the fix: GetConfigForUrlAsync is called on IStoreConfigProvider,
        // not via a type-cast to CombinedStoreConfigProvider
        const string url = "https://example.com/product";
        var userId = Guid.NewGuid();
        var expectedResult = new ScrapingResult
        {
            Success = true,
            Name = "User Config Product",
            Price = 29.99m,
            Currency = "EUR"
        };

        _configProviderMock.Setup(x => x.GetConfigForUrlAsync(url, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoreConfig
            {
                Id = "user-store",
                Name = "User Store",
                DomainPatterns = ["example.com"],
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = [".price"],
                    NameSelectors = ["h1"],
                    ImageSelectors = ["img"]
                }
            });

        _httpServiceMock.Setup(x => x.ScrapeProductAsync(url, null, userId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.ScrapeProductAsync(url, null, userId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — GetConfigForUrlAsync called on the mock interface (not a downcast)
        _configProviderMock.Verify(x => x.GetConfigForUrlAsync(url, userId, It.IsAny<CancellationToken>()), Times.Once);
        _configProviderMock.Verify(x => x.GetConfigForUrl(It.IsAny<string>()), Times.Never);
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task ScrapeWithConfigAsync_WithCurrencyOverride_OverridesCurrency()
    {
        // Arrange
        const string url = "https://example.com/product";
        var config = new StoreConfig
        {
            Id = "test-store",
            Name = "Test Store",
            DomainPatterns = ["example.com"],
            Selectors = new StoreSelectorConfig
            {
                PriceSelectors = [".price"],
                NameSelectors = ["h1"],
                ImageSelectors = ["img"]
            },
            CurrencyOverride = "EUR"
        };

        var scrapeResult = new ScrapingResult
        {
            Success = true,
            Name = "Product",
            Price = 29.99m,
            Currency = "USD"
        };

        _httpServiceMock.Setup(x => x.ScrapeWithConfigAsync(url, config, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scrapeResult);

        // Act
        var result = await _service.ScrapeWithConfigAsync(url, config, TestContext.Current.CancellationToken);

        // Assert
        result.Currency.Should().Be("EUR");
        result.Price.Should().Be(29.99m);
        result.Name.Should().Be("Product");
    }

    [Fact]
    public async Task ScrapeWithConfigAsync_WithoutCurrencyOverride_KeepsOriginalCurrency()
    {
        // Arrange
        const string url = "https://example.com/product";
        var config = new StoreConfig
        {
            Id = "test-store",
            Name = "Test Store",
            DomainPatterns = ["example.com"],
            Selectors = new StoreSelectorConfig
            {
                PriceSelectors = [".price"],
                NameSelectors = ["h1"],
                ImageSelectors = ["img"]
            }
        };

        var scrapeResult = new ScrapingResult
        {
            Success = true,
            Name = "Product",
            Price = 29.99m,
            Currency = "USD"
        };

        _httpServiceMock.Setup(x => x.ScrapeWithConfigAsync(url, config, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scrapeResult);

        // Act
        var result = await _service.ScrapeWithConfigAsync(url, config, TestContext.Current.CancellationToken);

        // Assert
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task ScrapeProductAsync_WithCurrencyOverride_OverridesCurrency()
    {
        // Arrange
        const string url = "https://example.com/product";
        var userId = Guid.NewGuid();

        _configProviderMock.Setup(x => x.GetConfigForUrlAsync(url, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoreConfig
            {
                Id = "test-store",
                Name = "Test Store",
                DomainPatterns = ["example.com"],
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = [".price"],
                    NameSelectors = ["h1"],
                    ImageSelectors = ["img"]
                },
                CurrencyOverride = "GBP"
            });

        _httpServiceMock.Setup(x => x.ScrapeProductAsync(url, null, userId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Name = "Product",
                Price = 19.99m,
                Currency = "USD"
            });

        // Act
        var result = await _service.ScrapeProductAsync(url, null, userId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Currency.Should().Be("GBP");
        result.Price.Should().Be(19.99m);
    }

    [Fact]
    public async Task ReturnsFailure_WhenBothFail_PreservesErrorMessage()
    {
        // Arrange
        const string url = "https://example.com/product";
        var httpResult = ScrapingResult.Failure("HTTP connection timed out");
        var playwrightResult = ScrapingResult.Failure("Playwright browser crashed");

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        _httpServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(httpResult);
        _playwrightServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(playwrightResult);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — failure is returned and error message is not lost
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task FallsBackToPlaywright_WhenHttpReturnsSuccessFalse()
    {
        // Arrange — HTTP returns a failure result (Success=false) without throwing
        const string url = "https://example.com/product";
        var httpResult = ScrapingResult.Failure("Selector not found on page");
        var playwrightResult = new ScrapingResult
        {
            Success = true,
            Name = "Recovered Product",
            Price = 29.99m,
            Currency = "USD"
        };

        _configProviderMock.Setup(x => x.GetConfigForUrl(url))
            .Returns((StoreConfig?)null);

        _httpServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(httpResult);
        _playwrightServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(playwrightResult);

        // Act
        var result = await _service.ScrapeProductAsync(url, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — Playwright fallback was attempted and succeeded
        result.Success.Should().BeTrue();
        result.Name.Should().Be("Recovered Product");
        _httpServiceMock.Verify(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        _playwrightServiceMock.Verify(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

}
