using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Moq;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Stores;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Api.Tests.Unit.Stores;

public class TestStoreHandlerTests
{
    private readonly Mock<IStoreConfigProvider> _configProviderMock = new();
    private readonly Mock<IScrapingService> _scrapingServiceMock = new();
    private readonly TestStore.Handler _handler;
    private readonly Guid _testUserId = Guid.NewGuid();

    public TestStoreHandlerTests()
    {
        _handler = new TestStore.Handler(_configProviderMock.Object, _scrapingServiceMock.Object, NullLogger<TestStore.Handler>.Instance);
    }

    private static StoreConfig CreateStoreConfig(string id = "my-store") => new()
    {
        Id = id,
        Name = "My Store",
        DomainPatterns = ["mystore.com"],
        Selectors = new StoreSelectorConfig
        {
            PriceSelectors = [".price"],
            NameSelectors = ["h1"],
            ImageSelectors = ["img.product"]
        }
    };

    [Fact]
    public async Task Handle_WithValidStoreAndUrl_ReturnsSuccessfulResult()
    {
        // Arrange
        var config = CreateStoreConfig();
        _configProviderMock.Setup(x => x.GetConfigsForUserAsync(_testUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StoreConfig> { config });

        _scrapingServiceMock.Setup(x => x.ScrapeWithConfigAsync("https://mystore.com/product/1", config, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Name = "Test Product",
                Price = 29.99m,
                Currency = "USD",
                ImageUrl = "https://mystore.com/img.jpg",
                DetectedSelector = ".price",
                StoreId = "my-store"
            });

        var command = new TestStore.Command("my-store", "https://mystore.com/product/1") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.ExtractedName.Should().Be("Test Product");
        result.ExtractedPrice.Should().Be(29.99m);
        result.Currency.Should().Be("USD");
        result.ExtractedImageUrl.Should().Be("https://mystore.com/img.jpg");
        result.DetectedSelector.Should().Be(".price");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithNonExistentStore_ThrowsNotFoundException()
    {
        // Arrange
        _configProviderMock.Setup(x => x.GetConfigsForUserAsync(_testUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StoreConfig>());

        var command = new TestStore.Command("nonexistent", "https://example.com") { UserId = _testUserId };

        // Act
        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Store 'nonexistent' not found");
    }

    [Fact]
    public async Task Handle_WithScrapingFailure_ReturnsFailureResult()
    {
        // Arrange
        var config = CreateStoreConfig();
        _configProviderMock.Setup(x => x.GetConfigsForUserAsync(_testUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StoreConfig> { config });

        _scrapingServiceMock.Setup(x => x.ScrapeWithConfigAsync("https://mystore.com/bad-page", config, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScrapingResult.Failure("Could not extract price from page"));

        var command = new TestStore.Command("my-store", "https://mystore.com/bad-page") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Could not extract price from page");
        result.ExtractedName.Should().BeNull();
        result.ExtractedPrice.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithBuiltInStore_ReturnsSuccessfulResult()
    {
        // Arrange
        var config = CreateStoreConfig("amazon");
        _configProviderMock.Setup(x => x.GetConfigsForUserAsync(_testUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StoreConfig> { config });

        _scrapingServiceMock.Setup(x => x.ScrapeWithConfigAsync("https://amazon.com/dp/B123", config, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Name = "Amazon Product",
                Price = 49.99m,
                Currency = "USD",
                DetectedSelector = "#priceblock_ourprice",
                StoreId = "amazon"
            });

        var command = new TestStore.Command("amazon", "https://amazon.com/dp/B123") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.ExtractedName.Should().Be("Amazon Product");
        result.ExtractedPrice.Should().Be(49.99m);
    }

    [Fact]
    public async Task Handle_WithCaseInsensitiveStoreId_FindsStore()
    {
        // Arrange
        var config = CreateStoreConfig("My-Store");
        _configProviderMock.Setup(x => x.GetConfigsForUserAsync(_testUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StoreConfig> { config });

        _scrapingServiceMock.Setup(x => x.ScrapeWithConfigAsync(It.IsAny<string>(), config, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Price = 10m, Currency = "USD" });

        var command = new TestStore.Command("my-store", "https://example.com") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
    }
}
