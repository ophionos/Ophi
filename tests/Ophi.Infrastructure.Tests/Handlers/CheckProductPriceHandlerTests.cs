using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Commands;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Webhooks;
using Ophi.Worker.Handlers;
using Ophi.Worker.Settings;
using Wolverine;

namespace Ophi.Infrastructure.Tests.Handlers;

public class CheckProductPriceHandlerTests : IDisposable
{
    private readonly Mock<IScrapingService> _scrapingServiceMock;
    private readonly Mock<ILogger> _loggerMock;
    private readonly Mock<IOptions<WorkerSettings>> _workerSettingsMock;
    private readonly Mock<IWebhookDispatchService> _webhookDispatchServiceMock = new();
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Guid _testUserId;

    public CheckProductPriceHandlerTests()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        _connection = connection;

        var options = new DbContextOptionsBuilder<OphiDbContext>()
            .UseSqlite(connection)
            .Options;

        _dbContext = new OphiDbContext(options);
        _dbContext.Database.EnsureCreated();

        // Create a test user for FK constraints
        _testUserId = Guid.NewGuid();
        var testUser = new User
        {
            Id = _testUserId,
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hash"
        };
        _dbContext.Users.Add(testUser);
        _dbContext.SaveChanges();

        _scrapingServiceMock = new Mock<IScrapingService>();
        _loggerMock = new Mock<ILogger>();
        _workerSettingsMock = new Mock<IOptions<WorkerSettings>>();

        // Default settings
        _workerSettingsMock.Setup(x => x.Value).Returns(new WorkerSettings());
    }

    [Fact]
    public async Task HandleAsync_WithSuccessfulScrape_UpdatesProductPrice()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            failureCount: 2); // Should reset to 0
        product.CurrentPrice = 100m;
        productUrl.CurrentPrice = 100m;
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Price = 89.99m,
                Currency = "USD"
            });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        var result = await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.Status.Should().Be(ProductStatus.Active);

        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.CurrentPrice.Should().Be(89.99m);
        updatedUrl.FailureCount.Should().Be(0);
        updatedUrl.LastError.Should().BeNull();

        result.Should().NotBeNull();
        result.ProductId.Should().Be(product.Id);
        result.NewPrice.Should().Be(89.99m);
        result.OldPrice.Should().Be(100m);
    }

    [Fact]
    public async Task HandleAsync_WithSuccessfulScrape_UpdatesLastCheckedAt()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product");
        var beforeCheck = DateTime.UtcNow;
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Price = 89.99m,
                Currency = "USD"
            });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.LastCheckedAt.Should().BeOnOrAfter(beforeCheck);
    }

    [Fact]
    public async Task HandleAsync_WithFailedScrape_ReachesMaxFailures_SetsErrorStatus()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            productStatus: ProductStatus.Active, failureCount: 0);
        // Start at MaxFailures - 1 so the handler increments exactly to MaxFailures,
        // triggering the == check. Other-URL AllAsync is vacuously true (no other URLs).
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Set max failures to 1 so it fails immediately
        _workerSettingsMock.Setup(x => x.Value).Returns(new WorkerSettings { MaxFailuresBeforeError = 1 });

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = false,
                Error = "Failed to extract price"
            });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        var result = await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.Status.Should().Be(ProductStatus.Error);

        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.FailureCount.Should().Be(1);
        updatedUrl.LastError.Should().Be("Failed to extract price");

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WithFailedScrape_BelowMaxFailures_IncrementsCount()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            productStatus: ProductStatus.Active);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Default max failures is 3

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = false,
                Error = "Temporary failure"
            });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        var result = await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.Status.Should().Be(ProductStatus.Active); // Still active

        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.FailureCount.Should().Be(1);
        updatedUrl.LastError.Should().Be("Temporary failure");

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WithNonActiveProduct_ReturnsNull()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            productStatus: ProductStatus.Error);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        var result = await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeNull();
        _scrapingServiceMock.Verify(
            x => x.ScrapeProductAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithNonExistentProduct_ReturnsNull()
    {
        // Arrange
        var command = new CheckProductUrlPriceCommand(Guid.NewGuid());

        // Act
        var result = await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WithFailedScrape_ReachesMaxFailures_CreatesNotification()
    {
        // Arrange — start at MaxFailures-1; the handler increments to MaxFailures == threshold
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            productStatus: ProductStatus.Active, failureCount: 0);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _workerSettingsMock.Setup(x => x.Value).Returns(new WorkerSettings { MaxFailuresBeforeError = 1 });

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = false,
                Error = "Failed to extract price"
            });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var notification = await _dbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification.Should().NotBeNull();
        notification.Type.Should().Be(NotificationType.ScrapeError);
        notification.UserId.Should().Be(_testUserId);
        notification.ProductId.Should().Be(product.Id);
        notification.Title.Should().Contain("Test Product");
    }

    [Fact]
    public async Task HandleAsync_WithFailedScrape_BelowMaxFailures_DoesNotCreateNotification()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            productStatus: ProductStatus.Active);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Default max failures is 3, so first failure won't create notification
        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = false,
                Error = "Temporary failure"
            });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var count = await _dbContext.Notifications.CountAsync(TestContext.Current.CancellationToken);
        count.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_WithSuccessfulScrape_CreatesScrapeLog()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product");
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Price = 89.99m,
                Currency = "USD"
            });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var scrapeLog = await _dbContext.ScrapeLogs.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        scrapeLog.Should().NotBeNull();
        scrapeLog.Success.Should().BeTrue();
        scrapeLog.Price.Should().Be(89.99m);
        scrapeLog.Error.Should().BeNull();
        scrapeLog.ProductId.Should().Be(product.Id);
        scrapeLog.ProductUrlId.Should().Be(productUrl.Id);
        scrapeLog.DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task HandleAsync_WithFailedScrape_CreatesScrapeLog()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            productStatus: ProductStatus.Active);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = false,
                Error = "Temporary failure"
            });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var scrapeLog = await _dbContext.ScrapeLogs.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        scrapeLog.Should().NotBeNull();
        scrapeLog.Success.Should().BeFalse();
        scrapeLog.Price.Should().BeNull();
        scrapeLog.Error.Should().Be("Temporary failure");
        scrapeLog.ProductId.Should().Be(product.Id);
        scrapeLog.ProductUrlId.Should().Be(productUrl.Id);
    }

    [Fact]
    public async Task HandleAsync_WithSuspiciousRedirect_IncrementsSuspiciousCount()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/products/123");
        product.CurrentPrice = 100m;
        productUrl.CurrentPrice = 100m;
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Price = 99m,
                Currency = "USD",
                FinalUrl = "https://other-domain.com/"
            });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        var result = await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.SuspiciousCount.Should().Be(1);
        updatedUrl.Status.Should().Be(ProductUrlStatus.Suspicious);
        updatedUrl.SuspiciousReason.Should().Contain("different domain");

        // First warning still updates price
        result.Should().NotBeNull();
        updatedUrl.CurrentPrice.Should().Be(99m);

        // First warning creates notification
        var notification = await _dbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification.Should().NotBeNull();
        notification!.Type.Should().Be(NotificationType.UrlHealth);
        notification.Title.Should().Contain("Suspicious scrape");
    }

    [Fact]
    public async Task HandleAsync_AtSuspiciousThreshold_PausesUrlAndDoesNotUpdatePrice()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/products/123",
            suspiciousCount: 2, urlStatus: ProductUrlStatus.Suspicious); // Will reach threshold of 3
        product.CurrentPrice = 100m;
        productUrl.CurrentPrice = 100m;
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Price = 5m,
                Currency = "USD",
                FinalUrl = "https://other-domain.com/"
            });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        var result = await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.Status.Should().Be(ProductUrlStatus.Paused);
        updatedUrl.SuspiciousCount.Should().Be(3);

        // Price should NOT have been updated (avoid corruption)
        updatedUrl.CurrentPrice.Should().Be(100m);

        // Should return null (no PriceUpdatedEvent)
        result.Should().BeNull();

        // Should create UrlHealth notification
        var notification = await _dbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification.Should().NotBeNull();
        notification!.Type.Should().Be(NotificationType.UrlHealth);
        notification.Title.Should().Contain("paused");
    }

    [Fact]
    public async Task HandleAsync_AntiBotPauseOfAnomalousUrl_ClearsTheProductFlag()
    {
        // Arrange — the URL's last price was an anomaly (warned, but kept). Blocked at the threshold,
        // the URL is paused; a paused URL no longer counts, and nothing re-scrapes it to clear the flag.
        var maxFailures = new WorkerSettings().MaxFailuresBeforeError;
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            productAnomalous: true, urlAnomalous: true, failureCount: maxFailures - 1);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = false, ErrorCategory = ScrapeErrorCategory.AntiBot });

        // Act
        await CheckProductPriceHandler.HandleAsync(
            new CheckProductUrlPriceCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.Status.Should().Be(ProductUrlStatus.Paused);
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.HasPriceAnomaly.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_AtSuspiciousThresholdWithAnomalousPrice_DoesNotFlagProductFromThePausedUrl()
    {
        // Arrange — 100 -> 5 is a price anomaly, but the same scrape auto-pauses the URL, and paused
        // URLs never count toward the product flag (Product.RecomputePriceAnomaly). The flag was
        // computed before the pause and stayed set until a sibling's next successful scrape.
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/products/123",
            suspiciousCount: 2, urlStatus: ProductUrlStatus.Suspicious);
        product.CurrentPrice = 100m;
        productUrl.CurrentPrice = 100m;
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Price = 5m, Currency = "USD", FinalUrl = "https://other-domain.com/" });

        // Act
        await CheckProductPriceHandler.HandleAsync(
            new CheckProductUrlPriceCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.Status.Should().Be(ProductUrlStatus.Paused);
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.HasPriceAnomaly.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_CleanScrapeAfterSuspicious_ResetsSuspiciousState()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/products/123",
            suspiciousCount: 2, urlStatus: ProductUrlStatus.Suspicious, suspiciousReason: "Some reason");
        product.CurrentPrice = 100m;
        productUrl.CurrentPrice = 100m;
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Price = 95m,
                Currency = "USD",
                FinalUrl = "https://example.com/products/123"
            });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        var result = await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.SuspiciousCount.Should().Be(0);
        updatedUrl.Status.Should().Be(ProductUrlStatus.Active);
        updatedUrl.SuspiciousReason.Should().BeNull();
        updatedUrl.CurrentPrice.Should().Be(95m);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_PausedUrl_SkipsScrape()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/products/123",
            urlStatus: ProductUrlStatus.Paused);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        var result = await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeNull();
        _scrapingServiceMock.Verify(
            x => x.ScrapeProductAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MultipleUrlsDifferentCurrencies_IgnoresUrlsOutsideProductCurrency()
    {
        // Arrange
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Multi-Currency Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        // URL-A: already has a price of $50 USD
        var urlA = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://us-store.com/product",
            Currency = "USD",
            CurrentPrice = 50m
        };
        // URL-B: will be scraped and return £30 GBP
        var urlB = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://uk-store.com/product",
            Currency = "GBP"
        };
        product.CurrentPrice = 50m;
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.AddRange(urlA, urlB);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(urlB.Url, urlB.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Price = 30m,
                Currency = "GBP"
            });

        var command = new CheckProductUrlPriceCommand(urlB.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert — GBP 30 is numerically smaller than USD 50 but is NOT a cheaper listing, and
        // adopting it would silently re-denominate the product (and its alert comparisons). The
        // product is priced in USD, so only the USD URL competes for the MIN.
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.CurrentPrice.Should().Be(50m);
        updatedProduct.Currency.Should().Be("USD");

        // The scraped URL still records its own price in its own currency.
        var updatedUrlB = await _dbContext.ProductUrls.FindAsync([urlB.Id], TestContext.Current.CancellationToken);
        updatedUrlB!.CurrentPrice.Should().Be(30m);
        updatedUrlB.Currency.Should().Be("GBP");
    }

    [Fact]
    public async Task HandleAsync_MultiUrl_NonMinUrlScraped_EventCarriesProductMinAndUrlValue()
    {
        // Lockdown for the PriceUpdatedEvent semantics fix: when a non-MIN URL is scraped,
        // the event's NewPrice must be the product MIN (unchanged) — not the scraped URL value —
        // and the URL-level fields must carry the just-scraped value for the history recorder.
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Multi-URL Product",
            Currency = "USD",
            CurrentPrice = 30m,
            Status = ProductStatus.Active
        };
        // urlA is the existing MIN at $30
        var urlA = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://cheap.com/product",
            Currency = "USD",
            CurrentPrice = 30m
        };
        // urlB will be scraped and return $90 — more expensive than urlA, so product MIN stays at $30
        var urlB = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://expensive.com/product",
            Currency = "USD",
            CurrentPrice = 95m
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.AddRange(urlA, urlB);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(urlB.Url, urlB.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Price = 90m, Currency = "USD" });

        var command = new CheckProductUrlPriceCommand(urlB.Id);

        var result = await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.OldPrice.Should().Be(30m);              // product MIN before
        result.NewPrice.Should().Be(30m);              // product MIN after (unchanged)
        result.Currency.Should().Be("USD");            // product currency
        result.ProductUrlId.Should().Be(urlB.Id);
        result.UrlPrice.Should().Be(90m);              // URL-level scraped value
        result.UrlCurrency.Should().Be("USD");
    }

    #region Out-of-Stock Handling

    [Fact]
    public async Task HandleAsync_WithOutOfStock_SetsIsOutOfStockTrue()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product");
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, IsOutOfStock = true, Price = null, ErrorCategory = ScrapeErrorCategory.OutOfStock });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.IsOutOfStock.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WithOutOfStockOnUnpricedUrl_AdoptsTheScrapedCurrency()
    {
        // Arrange — a URL added to an existing product has no price yet; without this its first
        // out-of-stock check left the "USD" default in place for a EUR listing.
        var (product, productUrl) = CreateProduct("Test Product", "https://example.de/product");
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, IsOutOfStock = true, Price = null, Currency = "EUR", ErrorCategory = ScrapeErrorCategory.OutOfStock });

        // Act
        await CheckProductPriceHandler.HandleAsync(
            new CheckProductUrlPriceCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task HandleAsync_WithOutOfStock_PreservesLastKnownPrice()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product");
        productUrl.CurrentPrice = 49.99m;
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, IsOutOfStock = true, Price = null, ErrorCategory = ScrapeErrorCategory.OutOfStock });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.CurrentPrice.Should().Be(49.99m);
    }

    [Fact]
    public async Task HandleAsync_WithOutOfStock_ResetsFailureCount()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            failureCount: 2);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, IsOutOfStock = true, Price = null, ErrorCategory = ScrapeErrorCategory.OutOfStock });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.FailureCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_OutOfStockTransition_CreatesNotification()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            isOutOfStock: false);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, IsOutOfStock = true, Price = null, ErrorCategory = ScrapeErrorCategory.OutOfStock });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var notification = await _dbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification.Should().NotBeNull();
        notification!.Type.Should().Be(NotificationType.OutOfStock);
        notification.UserId.Should().Be(_testUserId);
        notification.ProductId.Should().Be(product.Id);
        notification.Title.Should().Contain("Out of stock");
    }

    [Fact]
    public async Task HandleAsync_AlreadyOutOfStock_DoesNotDuplicateNotification()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            isOutOfStock: true);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, IsOutOfStock = true, Price = null, ErrorCategory = ScrapeErrorCategory.OutOfStock });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var count = await _dbContext.Notifications.CountAsync(TestContext.Current.CancellationToken);
        count.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_BackInStock_ClearsOutOfStockFlag()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            isOutOfStock: true);
        productUrl.CurrentPrice = 50m;
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Price = 39.99m, Currency = "USD", IsOutOfStock = false });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.IsOutOfStock.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_BackInStock_CreatesBackInStockNotification()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            isOutOfStock: true);
        productUrl.CurrentPrice = 50m;
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Price = 39.99m, Currency = "USD", IsOutOfStock = false });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var notification = await _dbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification.Should().NotBeNull();
        notification!.Type.Should().Be(NotificationType.BackInStock);
        notification.UserId.Should().Be(_testUserId);
        notification.ProductId.Should().Be(product.Id);
        notification.Title.Should().Contain("Back in stock");
    }

    [Fact]
    public async Task HandleAsync_WithRateLimited_DoesNotIncrementFailureCount()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            failureCount: 0);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScrapingResult.Failure("HTTP 429", ScrapeErrorCategory.RateLimited, 429));

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.FailureCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_With404Error_SetsDescriptiveLastError()
    {
        // Arrange
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product");
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScrapingResult.Failure("HTTP 404", ScrapeErrorCategory.NotFound, 404));

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.LastError.Should().Contain("Page not found");
    }

    #endregion

    #region Price Anomaly Flag

    [Fact]
    public async Task HandleAsync_WithPriceAnomaly_SetsHasPriceAnomalyTrue()
    {
        // Arrange — price jumps from 100 to 200 (100% change, above default 70% threshold)
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product");
        product.CurrentPrice = 100m;
        productUrl.CurrentPrice = 100m;
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Price = 200m, Currency = "USD" });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.HasPriceAnomaly.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WithCleanScrape_ClearsHasPriceAnomaly()
    {
        // Arrange — product had anomaly, now gets a normal price change
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            productAnomalous: true);
        product.CurrentPrice = 100m;
        productUrl.CurrentPrice = 100m;
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Price = 95m, Currency = "USD" });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.HasPriceAnomaly.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WithUserAnomalyThreshold_UsesUserSetting()
    {
        // Arrange — user threshold 10%, price changes 15% (above 10% but below global 70%)
        var user = await _dbContext.Users.FindAsync([_testUserId], TestContext.Current.CancellationToken);
        user!.AnomalyThresholdPercent = 10;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product");
        product.CurrentPrice = 100m;
        productUrl.CurrentPrice = 100m;
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Price = 115m, Currency = "USD" });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert — 15% change exceeds user's 10% threshold
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.HasPriceAnomaly.Should().BeTrue();
    }

    #endregion

    #region User Auto-Pause After Failures

    [Fact]
    public async Task HandleAsync_WithUserAutoPauseAfterFailures_UsesUserThreshold()
    {
        // Arrange — user sets auto-pause at 2 failures (lower than global default of 3)
        var user = await _dbContext.Users.FindAsync([_testUserId], TestContext.Current.CancellationToken);
        user!.AutoPauseAfterFailures = 2;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            productStatus: ProductStatus.Active, failureCount: 1);
        // One more will reach threshold of 2
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = false, Error = "Parse error" });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert — user threshold of 2 reached, product should be Error
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.Status.Should().Be(ProductStatus.Error);

        var notification = await _dbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification.Should().NotBeNull();
        notification!.Type.Should().Be(NotificationType.ScrapeError);
    }

    [Fact]
    public async Task HandleAsync_WithHighUserAutoPauseThreshold_DoesNotErrorAtGlobalThreshold()
    {
        // Arrange — user sets high threshold of 10, global is 3
        var user = await _dbContext.Users.FindAsync([_testUserId], TestContext.Current.CancellationToken);
        user!.AutoPauseAfterFailures = 10;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            productStatus: ProductStatus.Active, failureCount: 2);
        // Would reach global threshold of 3, but user is 10
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = false, Error = "Temporary failure" });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert — failure count is 3 (at global threshold) but user threshold is 10, no Error
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.Status.Should().Be(ProductStatus.Active);

        var notificationCount = await _dbContext.Notifications.CountAsync(TestContext.Current.CancellationToken);
        notificationCount.Should().Be(0);
    }

    #endregion

    #region Failure-threshold latch

    [Fact]
    public async Task HandleAsync_WhenFailureCountAlreadyPastLoweredThreshold_StillNotifies()
    {
        // Arrange — the URL accumulated 5 failures under a threshold of 10, then the user lowered
        // their auto-pause setting to 3. An exact-equality check would never match again, so the
        // URL would fail silently forever.
        var user = await _dbContext.Users.FindAsync([_testUserId], TestContext.Current.CancellationToken);
        user!.AutoPauseAfterFailures = 3;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            failureCount: 5);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = false, Error = "boom", ErrorCategory = ScrapeErrorCategory.NetworkError });

        // Act
        await CheckProductPriceHandler.HandleAsync(
            new CheckProductUrlPriceCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var notification = await _dbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification.Should().NotBeNull();
        notification!.Type.Should().Be(NotificationType.ScrapeError);

        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.FailureNotified.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyNotifiedForStreak_DoesNotNotifyAgain()
    {
        // Guard against the naive '>=' fix: a permanently-failing URL must not re-notify every cycle.
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            failureCount: 5, failureNotified: true);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = false, Error = "boom", ErrorCategory = ScrapeErrorCategory.NetworkError });

        // Act
        await CheckProductPriceHandler.HandleAsync(
            new CheckProductUrlPriceCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var notificationCount = await _dbContext.Notifications.CountAsync(TestContext.Current.CancellationToken);
        notificationCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_AfterSuccessfulScrape_ClearsFailureNotifiedLatch()
    {
        // A new streak must be able to notify again.
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            failureCount: 5, failureNotified: true);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Price = 42m, Currency = "USD" });

        // Act
        await CheckProductPriceHandler.HandleAsync(
            new CheckProductUrlPriceCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.FailureNotified.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenThresholdRaisedAfterNotifying_StillMarksProductAsError()
    {
        // Arrange — the URL notified at a threshold of 3, so the latch is set. The user then RAISES
        // AutoPauseAfterFailures to 6 and the URL keeps failing up to the new threshold. Status
        // reconciliation must not ride on the notification latch: the product has no working URL
        // left, so it belongs in Error regardless of whether this streak already notified.
        var user = await _dbContext.Users.FindAsync([_testUserId], TestContext.Current.CancellationToken);
        user!.AutoPauseAfterFailures = 6;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            failureCount: 5, failureNotified: true);
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = false, Error = "boom", ErrorCategory = ScrapeErrorCategory.NetworkError });

        // Act — sixth failure, reaching the raised threshold.
        await CheckProductPriceHandler.HandleAsync(
            new CheckProductUrlPriceCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert — Error status is reconciled, but the latch still suppresses a second notification.
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.Status.Should().Be(ProductStatus.Error);

        var notificationCount = await _dbContext.Notifications.CountAsync(TestContext.Current.CancellationToken);
        notificationCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_WhenOneUrlStillHealthy_DoesNotMarkProductAsError()
    {
        // Guard for the hoist above: reconciling status outside the latch must not start erroring
        // products that still have a working URL.
        var (product, failingUrl) = CreateProduct("Test Product", "https://example.com/failing",
            failureCount: 5, failureNotified: true);
        var healthyUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/healthy",
            Currency = "USD",
            CurrentPrice = 42m
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.AddRange(failingUrl, healthyUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(failingUrl.Url, failingUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = false, Error = "boom", ErrorCategory = ScrapeErrorCategory.NetworkError });

        // Act
        await CheckProductPriceHandler.HandleAsync(
            new CheckProductUrlPriceCommand(failingUrl.Id), _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.Status.Should().Be(ProductStatus.Active);
    }

    #endregion

    #region Product-level anomaly flag

    [Fact]
    public async Task HandleAsync_WithCleanScrape_DoesNotClearAnomalyFlaggedOnSiblingUrl()
    {
        // Arrange — sibling URL is anomalous; a clean scrape of this URL must not erase the
        // product-level warning while the anomalous URL still contributes to the price.
        var (product, cleanUrl) = CreateProduct("Test Product", "https://example.com/clean",
            productAnomalous: true);
        var anomalousUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/anomalous",
            Currency = "USD",
            CurrentPrice = 5m,
            HasPriceAnomaly = true
        };
        cleanUrl.CurrentPrice = 100m;
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.AddRange(cleanUrl, anomalousUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(cleanUrl.Url, cleanUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Price = 99m, Currency = "USD" });

        // Act
        await CheckProductPriceHandler.HandleAsync(
            new CheckProductUrlPriceCommand(cleanUrl.Id), _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.HasPriceAnomaly.Should().BeTrue();

        var updatedCleanUrl = await _dbContext.ProductUrls.FindAsync([cleanUrl.Id], TestContext.Current.CancellationToken);
        updatedCleanUrl!.HasPriceAnomaly.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WithCleanScrapeAndNoOtherAnomaly_ClearsProductAnomalyFlag()
    {
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/product",
            productAnomalous: true, urlAnomalous: true);
        productUrl.CurrentPrice = 100m;
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Price = 99m, Currency = "USD" });

        // Act
        await CheckProductPriceHandler.HandleAsync(
            new CheckProductUrlPriceCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.HasPriceAnomaly.Should().BeFalse();
    }

    #endregion

    #region Paused-URL aggregation

    [Fact]
    public async Task HandleAsync_WithPausedSiblingUrl_ExcludesItFromProductMinimum()
    {
        // Arrange — a paused URL holds a stale-but-lower price. PriceCheckDispatcher never
        // re-scrapes paused URLs, so if it kept defining the product MIN the product would show
        // that frozen price forever.
        var (product, activeUrl) = CreateProduct("Test Product", "https://example.com/active");
        var pausedUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/paused",
            Currency = "USD",
            CurrentPrice = 50m,
            Status = ProductUrlStatus.Paused
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(activeUrl);
        _dbContext.ProductUrls.Add(pausedUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(activeUrl.Url, activeUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Price = 80m, Currency = "USD" });

        var command = new CheckProductUrlPriceCommand(activeUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert — the live URL's 80 defines the product price, not the paused URL's 50
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.CurrentPrice.Should().Be(80m);
    }

    [Fact]
    public async Task HandleAsync_WithActiveSiblingUrl_StillIncludesItInProductMinimum()
    {
        // Guard against over-correcting: non-paused siblings must keep contributing to the MIN.
        var (product, scrapedUrl) = CreateProduct("Test Product", "https://example.com/scraped");
        var cheaperSibling = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/cheaper",
            Currency = "USD",
            CurrentPrice = 50m,
            Status = ProductUrlStatus.Active
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(scrapedUrl);
        _dbContext.ProductUrls.Add(cheaperSibling);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(scrapedUrl.Url, scrapedUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Price = 80m, Currency = "USD" });

        var command = new CheckProductUrlPriceCommand(scrapedUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.CurrentPrice.Should().Be(50m);
    }

    [Fact]
    public async Task HandleAsync_AtSuspiciousThreshold_DropsThePausedUrlFromProductMinimum()
    {
        // Arrange — the URL being paused holds the product MIN (50). Once paused, nothing re-scrapes
        // it, so the live sibling's 80 must take over now rather than at the sibling's next scrape.
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/suspicious",
            suspiciousCount: 2, urlStatus: ProductUrlStatus.Suspicious);
        product.CurrentPrice = 50m;
        productUrl.CurrentPrice = 50m;
        var sibling = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/sibling",
            Currency = "USD",
            CurrentPrice = 80m
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.AddRange(productUrl, sibling);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Price = 5m, Currency = "USD", FinalUrl = "https://other-domain.com/" });

        // Act
        await CheckProductPriceHandler.HandleAsync(
            new CheckProductUrlPriceCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.CurrentPrice.Should().Be(80m);
        updatedProduct.PreviousPrice.Should().Be(50m);
    }

    [Fact]
    public async Task HandleAsync_AtSuspiciousThresholdOnOnlyUrl_ClearsProductPrice()
    {
        // Arrange — with its only URL paused, the product has no live price: the same outcome as
        // RemoveProductUrl leaving no priced live URL.
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/suspicious",
            suspiciousCount: 2, urlStatus: ProductUrlStatus.Suspicious);
        product.CurrentPrice = 100m;
        productUrl.CurrentPrice = 100m;
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Price = 5m, Currency = "USD", FinalUrl = "https://other-domain.com/" });

        // Act
        await CheckProductPriceHandler.HandleAsync(
            new CheckProductUrlPriceCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert — the URL keeps its last price; only the product headline price goes
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.CurrentPrice.Should().BeNull();
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.CurrentPrice.Should().Be(100m);
    }

    [Fact]
    public async Task HandleAsync_AntiBotPause_DropsThePausedUrlFromProductMinimum()
    {
        // Arrange
        var maxFailures = new WorkerSettings().MaxFailuresBeforeError;
        var (product, productUrl) = CreateProduct("Test Product", "https://example.com/blocked",
            failureCount: maxFailures - 1);
        product.CurrentPrice = 50m;
        productUrl.CurrentPrice = 50m;
        var sibling = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/sibling",
            Currency = "USD",
            CurrentPrice = 80m
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.AddRange(productUrl, sibling);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = false, ErrorCategory = ScrapeErrorCategory.AntiBot });

        // Act
        await CheckProductPriceHandler.HandleAsync(
            new CheckProductUrlPriceCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object, _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrl.Id], TestContext.Current.CancellationToken);
        updatedUrl!.Status.Should().Be(ProductUrlStatus.Paused);
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.CurrentPrice.Should().Be(80m);
    }

    #endregion

    private (Product product, ProductUrl productUrl) CreateProduct(
        string name,
        string url,
        ProductStatus productStatus = ProductStatus.Active,
        bool productAnomalous = false,
        ProductUrlStatus urlStatus = ProductUrlStatus.Active,
        int failureCount = 0,
        bool failureNotified = false,
        int suspiciousCount = 0,
        string? suspiciousReason = null,
        bool isOutOfStock = false,
        bool urlAnomalous = false)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = name,
            Currency = "USD",
            Status = productStatus,
            HasPriceAnomaly = productAnomalous
        };
        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = url,
            Currency = "USD",
            Status = urlStatus,
            FailureCount = failureCount,
            FailureNotified = failureNotified,
            SuspiciousCount = suspiciousCount,
            SuspiciousReason = suspiciousReason,
            IsOutOfStock = isOutOfStock,
            HasPriceAnomaly = urlAnomalous
        };
        return (product, productUrl);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
