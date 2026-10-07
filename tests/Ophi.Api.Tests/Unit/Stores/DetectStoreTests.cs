using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using FluentValidation.TestHelper;
using Moq;
using Ophi.Api.Features.Stores;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Api.Tests.Unit.Stores;

public class DetectStoreValidatorTests
{
    private readonly DetectStore.Validator _validator = new();

    [Fact]
    public void Validate_WithEmptyUrl_ShouldFail()
    {
        var command = new DetectStore.Command("");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    [Fact]
    public void Validate_WithInvalidUrl_ShouldFail()
    {
        var command = new DetectStore.Command("not-a-url");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    [Fact]
    public void Validate_WithFtpUrl_ShouldFail()
    {
        var command = new DetectStore.Command("ftp://example.com");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    [Fact]
    public void Validate_WithValidHttpUrl_ShouldPass()
    {
        var command = new DetectStore.Command("http://example.com/product");
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithValidHttpsUrl_ShouldPass()
    {
        var command = new DetectStore.Command("https://example.com/product");
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("http://localhost/admin")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://10.0.0.1")]
    [InlineData("http://192.168.1.1")]
    [InlineData("http://172.16.0.1")]
    public void Validate_WithPrivateIpUrl_ShouldFail(string url)
    {
        var command = new DetectStore.Command(url);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }
}

public class DetectStoreHandlerTests
{
    private readonly Mock<IScrapingService> _scrapingServiceMock = new();
    private readonly Mock<IAutoCreateStoreService> _autoCreateServiceMock = new();
    private readonly DetectStore.Handler _handler;

    public DetectStoreHandlerTests()
    {
        _handler = new DetectStore.Handler(_scrapingServiceMock.Object, _autoCreateServiceMock.Object, NullLogger<DetectStore.Handler>.Instance);
    }

    [Fact]
    public async Task Handle_WithSuccessfulDetection_ReturnsSelectors()
    {
        // Arrange
        var url = "https://shop.example.com/product/1";
        var html = "<html><body><span class='price'>$29.99</span></body></html>";

        _scrapingServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, FetchedHtml = html });

        _autoCreateServiceMock.Setup(x => x.AnalyzeHtmlAsync(html, url, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutoCreateStoreResult
            {
                StoreName = "Example Shop",
                Domain = "shop.example.com",
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = [".price"],
                    NameSelectors = ["h1"],
                    ImageSelectors = ["meta[property='og:image']|content"],
                    PriceRegexPatterns = null
                }
            });

        var command = new DetectStore.Command(url);

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.StoreName.Should().Be("Example Shop");
        result.Domain.Should().Be("shop.example.com");
        result.Selectors.Should().NotBeNull();
        result.Selectors!.PriceSelectors.Should().Contain(".price");
        result.Selectors.NameSelectors.Should().Contain("h1");
        result.Selectors.ImageSelectors.Should().Contain("meta[property='og:image']|content");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithFailedScrape_ReturnsError()
    {
        // Arrange
        var url = "https://example.com/product";

        _scrapingServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScrapingResult.Failure("Connection timed out"));

        var command = new DetectStore.Command(url);

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Connection timed out");
        result.Selectors.Should().BeNull();
        result.StoreName.Should().BeNull();
        result.Domain.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithNoHtmlReturned_ReturnsError()
    {
        // Arrange
        var url = "https://example.com/product";

        _scrapingServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, FetchedHtml = null });

        var command = new DetectStore.Command(url);

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Failed to fetch page content");
    }

    [Fact]
    public async Task Handle_WithNoSelectorsDetected_ReturnsError()
    {
        // Arrange
        var url = "https://example.com/product";
        var html = "<html><body>No product info here</body></html>";

        _scrapingServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, FetchedHtml = html });

        _autoCreateServiceMock.Setup(x => x.AnalyzeHtmlAsync(html, url, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AutoCreateStoreResult?)null);

        var command = new DetectStore.Command(url);

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Could not detect store selectors from the page");
        result.Selectors.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithPriceRegexPatterns_IncludesThemInResponse()
    {
        // Arrange
        var url = "https://example.com/product";
        var html = "<html><body><span class='price'>$29.99</span></body></html>";

        _scrapingServiceMock.Setup(x => x.ScrapeProductAsync(url, null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, FetchedHtml = html });

        _autoCreateServiceMock.Setup(x => x.AnalyzeHtmlAsync(html, url, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutoCreateStoreResult
            {
                StoreName = "Example",
                Domain = "example.com",
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = [".price"],
                    NameSelectors = ["h1"],
                    ImageSelectors = ["img"],
                    PriceRegexPatterns = [@"""price""\s?:\s?""([^""]+)"""]
                }
            });

        var command = new DetectStore.Command(url);

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Selectors!.PriceRegexPatterns.Should().NotBeNull();
        result.Selectors.PriceRegexPatterns.Should().HaveCount(1);
    }
}
