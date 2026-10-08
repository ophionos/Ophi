using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Commands;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;
using Ophi.Worker.Handlers;
using Wolverine;

namespace Ophi.Infrastructure.Tests.Handlers;

public class ScrapeNewProductHandlerTests : IDisposable
{
    private readonly Mock<IScrapingService> _scrapingServiceMock;
    private readonly Mock<IAutoCreateStoreService> _autoCreateStoreServiceMock;
    private readonly Mock<IStoreConfigProvider> _configProviderMock;
    private readonly Mock<ILogger> _loggerMock;
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Guid _testUserId;

    public ScrapeNewProductHandlerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<OphiDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new OphiDbContext(options);
        _dbContext.Database.EnsureCreated();

        _scrapingServiceMock = new Mock<IScrapingService>();
        _autoCreateStoreServiceMock = new Mock<IAutoCreateStoreService>();
        _configProviderMock = new Mock<IStoreConfigProvider>();
        _loggerMock = new Mock<ILogger>();

        // Seed test user
        _testUserId = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = _testUserId,
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hash"
        });
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task HandleAsync_WithPendingProduct_UpdatesToActiveOnSuccess()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            UserId = _testUserId,
            Name = "Loading...",
            Currency = "USD",
            Status = ProductStatus.Pending
        };
        var productUrl = new ProductUrl
        {
            Id = productUrlId,
            ProductId = productId,
            Url = "https://example.com/product",
            Currency = "USD"
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Name = "Test Product",
                Price = 99.99m,
                Currency = "USD",
                ImageUrl = "https://example.com/image.jpg",
                DetectedSelector = ".price"
            });

        var command = new ScrapeProductUrlCommand(productUrlId);

        // Act
        var result = await ScrapeNewProductHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([productId], TestContext.Current.CancellationToken);
        updatedProduct!.Status.Should().Be(ProductStatus.Active);
        updatedProduct.Name.Should().Be("Test Product");
        updatedProduct.CurrentPrice.Should().Be(99.99m);
        updatedProduct.ImageUrl.Should().Be("https://example.com/image.jpg");

        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrlId], TestContext.Current.CancellationToken);
        updatedUrl!.LastError.Should().BeNull();

        result.Should().NotBeNull();
        result.ProductId.Should().Be(productId);
        result.NewPrice.Should().Be(99.99m);
    }

    [Fact]
    public async Task HandleAsync_WithPendingProduct_UpdatesToErrorOnFailure()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            UserId = _testUserId,
            Name = "Loading...",
            Currency = "USD",
            Status = ProductStatus.Pending
        };
        var productUrl = new ProductUrl
        {
            Id = productUrlId,
            ProductId = productId,
            Url = "https://example.com/product",
            Currency = "USD"
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = false,
                Error = "Failed to extract price"
            });

        var command = new ScrapeProductUrlCommand(productUrlId);

        // Act
        var result = await ScrapeNewProductHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([productId], TestContext.Current.CancellationToken);
        updatedProduct!.Status.Should().Be(ProductStatus.Error);

        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrlId], TestContext.Current.CancellationToken);
        updatedUrl!.LastError.Should().Be("Failed to extract price");

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WithNonPendingProduct_SkipsProcessing()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            UserId = _testUserId,
            Name = "Existing Product",
            Currency = "USD",
            Status = ProductStatus.Active // Not pending
        };
        var productUrl = new ProductUrl
        {
            Id = productUrlId,
            ProductId = productId,
            Url = "https://example.com/product",
            Currency = "USD"
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new ScrapeProductUrlCommand(productUrlId);

        // Act
        var result = await ScrapeNewProductHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeNull();
        _scrapingServiceMock.Verify(
            x => x.ScrapeProductAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithForceFlag_ProcessesNonPendingProduct()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            UserId = _testUserId,
            Name = "Existing Product",
            Currency = "USD",
            Status = ProductStatus.Active // Not pending
        };
        var productUrl = new ProductUrl
        {
            Id = productUrlId,
            ProductId = productId,
            Url = "https://example.com/product",
            Currency = "USD"
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Name = "Updated Product",
                Price = 79.99m,
                Currency = "USD"
            });

        var command = new ScrapeProductUrlCommand(productUrlId, Force: true);

        // Act
        var result = await ScrapeNewProductHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.NewPrice.Should().Be(79.99m);

        _scrapingServiceMock.Verify(
            x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithNonExistentProduct_ReturnsNull()
    {
        // Arrange
        var command = new ScrapeProductUrlCommand(Guid.NewGuid());

        // Act
        var result = await ScrapeNewProductHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WithSuccessfulScrape_CreatesScrapeLog()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            UserId = _testUserId,
            Name = "Loading...",
            Currency = "USD",
            Status = ProductStatus.Pending
        };
        var productUrl = new ProductUrl
        {
            Id = productUrlId,
            ProductId = productId,
            Url = "https://example.com/product",
            Currency = "USD"
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Name = "Test Product",
                Price = 99.99m,
                Currency = "USD"
            });

        var command = new ScrapeProductUrlCommand(productUrlId);

        // Act
        await ScrapeNewProductHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var scrapeLog = await _dbContext.ScrapeLogs.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        scrapeLog.Should().NotBeNull();
        scrapeLog.Success.Should().BeTrue();
        scrapeLog.Price.Should().Be(99.99m);
        scrapeLog.Error.Should().BeNull();
        scrapeLog.ProductId.Should().Be(productId);
        scrapeLog.ProductUrlId.Should().Be(productUrlId);
        scrapeLog.DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task HandleAsync_WithFailedScrape_CreatesScrapeLog()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            UserId = _testUserId,
            Name = "Loading...",
            Currency = "USD",
            Status = ProductStatus.Pending
        };
        var productUrl = new ProductUrl
        {
            Id = productUrlId,
            ProductId = productId,
            Url = "https://example.com/product",
            Currency = "USD"
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = false,
                Error = "Failed to extract price"
            });

        var command = new ScrapeProductUrlCommand(productUrlId);

        // Act
        await ScrapeNewProductHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var scrapeLog = await _dbContext.ScrapeLogs.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        scrapeLog.Should().NotBeNull();
        scrapeLog.Success.Should().BeFalse();
        scrapeLog.Price.Should().BeNull();
        scrapeLog.Error.Should().Be("Failed to extract price");
        scrapeLog.ProductId.Should().Be(productId);
        scrapeLog.ProductUrlId.Should().Be(productUrlId);
    }

    [Fact]
    public async Task HandleAsync_WithOutOfStockProduct_SetsActiveAndOosFlag()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            UserId = _testUserId,
            Name = "Loading...",
            Currency = "USD",
            Status = ProductStatus.Pending
        };
        var productUrl = new ProductUrl
        {
            Id = productUrlId,
            ProductId = productId,
            Url = "https://example.com/product",
            Currency = "USD"
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                IsOutOfStock = true,
                Name = "OOS Product",
                Currency = "USD"
            });

        var command = new ScrapeProductUrlCommand(productUrlId);

        // Act
        var result = await ScrapeNewProductHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([productId], TestContext.Current.CancellationToken);
        updatedProduct!.Status.Should().Be(ProductStatus.Active);
        updatedProduct.Name.Should().Be("OOS Product");

        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrlId], TestContext.Current.CancellationToken);
        updatedUrl!.IsOutOfStock.Should().BeTrue();
        updatedUrl.LastError.Should().BeNull();

        result.Should().BeNull("OOS should return null");
    }

    [Fact]
    public async Task HandleAsync_WithOutOfStockProduct_CreatesOosNotificationAndScrapeLog()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            UserId = _testUserId,
            Name = "Loading...",
            Currency = "USD",
            Status = ProductStatus.Pending
        };
        var productUrl = new ProductUrl
        {
            Id = productUrlId,
            ProductId = productId,
            Url = "https://example.com/product",
            Currency = "USD"
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                IsOutOfStock = true,
                Name = "OOS Product",
                Currency = "USD"
            });

        var command = new ScrapeProductUrlCommand(productUrlId);

        // Act
        await ScrapeNewProductHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var notification = await _dbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification.Should().NotBeNull();
        notification!.Type.Should().Be(NotificationType.OutOfStock);
        notification.Title.Should().Contain("OOS Product");

        var scrapeLog = await _dbContext.ScrapeLogs.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        scrapeLog.Should().NotBeNull();
        scrapeLog!.Success.Should().BeTrue();
        scrapeLog.IsOutOfStock.Should().BeTrue();
        scrapeLog.ProductId.Should().Be(productId);
        scrapeLog.ProductUrlId.Should().Be(productUrlId);
    }

    [Fact]
    public async Task HandleAsync_WhenScrapeThrowsException_SetsErrorStatusAndRethrows()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            UserId = _testUserId,
            Name = "Loading...",
            Currency = "USD",
            Status = ProductStatus.Pending
        };
        var productUrl = new ProductUrl
        {
            Id = productUrlId,
            ProductId = productId,
            Url = "https://example.com/product",
            Currency = "USD"
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Timeout"));

        var command = new ScrapeProductUrlCommand(productUrlId);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await ScrapeNewProductHandler.HandleAsync(
                command, _dbContext, _scrapingServiceMock.Object,
                _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
                TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken));

        var updatedProduct = await _dbContext.Products.FindAsync([productId], TestContext.Current.CancellationToken);
        updatedProduct!.Status.Should().Be(ProductStatus.Error);

        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrlId], TestContext.Current.CancellationToken);
        updatedUrl!.LastError.Should().Be("Timeout");
        updatedUrl.LastCheckedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task HandleAsync_WhenScrapeIsCancelled_RethrowsWithoutRecordingFailure()
    {
        // Cancellation is not a scrape failure: it must not mark the product Error or spend the
        // URL's failure budget. It escapes to the retry rule (docs/agent-notes.md § Messaging).
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Loading...",
            Currency = "USD",
            Status = ProductStatus.Pending
        };
        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/cancelled",
            Currency = "USD"
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await ScrapeNewProductHandler.HandleAsync(
                new ScrapeProductUrlCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object,
                _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
                TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken));

        _dbContext.ChangeTracker.Clear();
        var saved = await _dbContext.Products.Include(p => p.ProductUrls)
            .SingleAsync(p => p.Id == product.Id, TestContext.Current.CancellationToken);
        saved.Status.Should().Be(ProductStatus.Pending);
        saved.ProductUrls.Single().FailureCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_WithDetectedSelector_PersistsSelector()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            UserId = _testUserId,
            Name = "Loading...",
            Currency = "USD",
            Status = ProductStatus.Pending
        };
        var productUrl = new ProductUrl
        {
            Id = productUrlId,
            ProductId = productId,
            Url = "https://example.com/product",
            Currency = "USD"
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Name = "Test Product",
                Price = 99.99m,
                Currency = "USD",
                DetectedSelector = ".price-box"
            });

        var command = new ScrapeProductUrlCommand(productUrlId);

        // Act
        await ScrapeNewProductHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrlId], TestContext.Current.CancellationToken);
        updatedUrl!.Selector.Should().Be(".price-box");
    }

    [Fact]
    public async Task HandleAsync_ForcedRetryOnMultiUrlProduct_KeepsMinAcrossUrls()
    {
        // Arrange — RetryScrapeProductUrl publishes ScrapeProductUrlCommand with Force:true, which
        // bypasses this handler's "only when Pending" guard. On a multi-URL product the direct
        // assignment `product.CurrentPrice = result.Price.Value` discarded the MIN across siblings.
        var productId = Guid.NewGuid();
        var retriedUrlId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            UserId = _testUserId,
            Name = "Test Product",
            Currency = "USD",
            Status = ProductStatus.Active,
            CurrentPrice = 50m
        };
        var cheaperSibling = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Url = "https://example.com/cheap",
            Currency = "USD",
            CurrentPrice = 50m
        };
        var retriedUrl = new ProductUrl
        {
            Id = retriedUrlId,
            ProductId = productId,
            Url = "https://example.com/retried",
            Currency = "USD"
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.AddRange(cheaperSibling, retriedUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(retriedUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Name = "Test Product", Price = 80m, Currency = "USD" });

        var command = new ScrapeProductUrlCommand(retriedUrlId, Force: true);

        // Act
        var result = await ScrapeNewProductHandler.HandleAsync(
            command, _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert — the sibling's cheaper price still defines the product headline price.
        var updatedProduct = await _dbContext.Products.FindAsync([productId], TestContext.Current.CancellationToken);
        updatedProduct!.CurrentPrice.Should().Be(50m);

        var updatedUrl = await _dbContext.ProductUrls.FindAsync([retriedUrlId], TestContext.Current.CancellationToken);
        updatedUrl!.CurrentPrice.Should().Be(80m);

        // The event's product tier reports the aggregate; the URL tier reports what was scraped.
        result.Should().NotBeNull();
        result!.NewPrice.Should().Be(50m);
        result.UrlPrice.Should().Be(80m);
    }

    [Fact]
    public async Task HandleAsync_ForcedRetryOutOfStockInOtherCurrency_KeepsPricedCurrencies()
    {
        // Arrange — the manual retry (Force:true) reaches products that already have a price. The
        // out-of-stock branch keeps the last-known prices, so it must keep their denominations too.
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();
        _dbContext.Products.Add(new Product
        {
            Id = productId,
            UserId = _testUserId,
            Name = "Test Product",
            Currency = "USD",
            Status = ProductStatus.Active,
            CurrentPrice = 50m
        });
        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = productUrlId,
            ProductId = productId,
            Url = "https://example.com/product",
            Currency = "USD",
            CurrentPrice = 50m
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync("https://example.com/product", null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, IsOutOfStock = true, Name = "Test Product", Currency = "EUR" });

        // Act
        await ScrapeNewProductHandler.HandleAsync(
            new ScrapeProductUrlCommand(productUrlId, Force: true), _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([productId], TestContext.Current.CancellationToken);
        updatedProduct!.Currency.Should().Be("USD");
        updatedProduct.CurrentPrice.Should().Be(50m);

        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrlId], TestContext.Current.CancellationToken);
        updatedUrl!.Currency.Should().Be("USD");
        updatedUrl.IsOutOfStock.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_InitialScrapeOutOfStockInForeignCurrency_AdoptsTheCurrency()
    {
        // Nothing is priced yet, so the scraped currency is the only denomination information there is.
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();
        _dbContext.Products.Add(new Product
        {
            Id = productId,
            UserId = _testUserId,
            Name = "Loading...",
            Currency = "USD",
            Status = ProductStatus.Pending
        });
        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = productUrlId,
            ProductId = productId,
            Url = "https://example.de/product",
            Currency = "USD"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync("https://example.de/product", null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, IsOutOfStock = true, Name = "Produkt", Currency = "EUR" });

        // Act
        await ScrapeNewProductHandler.HandleAsync(
            new ScrapeProductUrlCommand(productUrlId), _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([productId], TestContext.Current.CancellationToken);
        updatedProduct!.Currency.Should().Be("EUR");
        var updatedUrl = await _dbContext.ProductUrls.FindAsync([productUrlId], TestContext.Current.CancellationToken);
        updatedUrl!.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task HandleAsync_InitialScrapeInForeignCurrency_ReAnchorsProductCurrency()
    {
        // A brand-new product defaults to Currency "USD"; its only URL scraping in EUR must
        // re-anchor the product rather than be filtered out by the same-currency aggregate rule.
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();
        _dbContext.Products.Add(new Product
        {
            Id = productId,
            UserId = _testUserId,
            Name = "Loading...",
            Currency = "USD",
            Status = ProductStatus.Pending
        });
        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = productUrlId,
            ProductId = productId,
            Url = "https://example.de/product",
            Currency = "USD"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync("https://example.de/product", null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Name = "Produkt", Price = 42m, Currency = "EUR" });

        // Act
        await ScrapeNewProductHandler.HandleAsync(
            new ScrapeProductUrlCommand(productUrlId), _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([productId], TestContext.Current.CancellationToken);
        updatedProduct!.CurrentPrice.Should().Be(42m);
        updatedProduct.Currency.Should().Be("EUR");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
