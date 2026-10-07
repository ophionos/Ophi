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

public class ScrapeNewProductAutoCreateTests : IDisposable
{
    private readonly Mock<IScrapingService> _scrapingServiceMock = new();
    private readonly Mock<IAutoCreateStoreService> _autoCreateStoreServiceMock = new();
    private readonly Mock<IStoreConfigProvider> _configProviderMock = new();
    private readonly Mock<ILogger> _loggerMock = new();
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Guid _testUserId;

    public ScrapeNewProductAutoCreateTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<OphiDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new OphiDbContext(options);
        _dbContext.Database.EnsureCreated();

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

    private (Product product, ProductUrl productUrl) CreatePendingProduct(string url = "https://newshop.com/product/1")
    {
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
            Url = url,
            Currency = "USD"
        };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        _dbContext.SaveChanges();
        return (product, productUrl);
    }

    [Fact]
    public async Task HandleAsync_WithGenericStoreAndHtml_TriggersAutoCreation()
    {
        // Arrange
        var (_, productUrl) = CreatePendingProduct();
        var html = "<html><body><span class='price'>$10.00</span></body></html>";

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true, Name = "Product", Price = 10.00m, Currency = "USD",
                StoreId = "generic", FetchedHtml = html
            });

        _autoCreateStoreServiceMock
            .Setup(x => x.AnalyzeHtmlAsync(html, productUrl.Url, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutoCreateStoreResult
            {
                StoreName = "Newshop",
                Domain = "newshop.com",
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = [".price"],
                    NameSelectors = ["h1"],
                    ImageSelectors = []
                }
            });

        // No existing store config for this domain
        _configProviderMock
            .Setup(x => x.GetConfigForUrlAsync(productUrl.Url, _testUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoreConfig?)null);

        // Act
        await ScrapeNewProductHandler.HandleAsync(
            new ScrapeProductUrlCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var storeConfig = await _dbContext.StoreConfigurations
            .FirstOrDefaultAsync(s => s.UserId == _testUserId, cancellationToken: TestContext.Current.CancellationToken);
        storeConfig.Should().NotBeNull();
        storeConfig.StoreId.Should().Be("auto-newshop-com");
        storeConfig.Name.Should().Be("Newshop");
        storeConfig.IsAutoCreated.Should().BeTrue();
        storeConfig.UserId.Should().Be(_testUserId);

        _configProviderMock.Verify(x => x.InvalidateCache(_testUserId), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_RequestsHtmlCapture_OnInitialScrape()
    {
        // The initial scrape must opt into HTML capture (captureHtml: true) so the generic Playwright
        // fallback can return the rendered DOM for auto store-config inference (issue #23). Without this
        // the Playwright path returns null HTML and auto-create silently never runs.
        var (_, productUrl) = CreatePendingProduct();
        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true, Name = "Product", Price = 10.00m, Currency = "USD", StoreId = "amazon"
            });

        await ScrapeNewProductHandler.HandleAsync(
            new ScrapeProductUrlCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        _scrapingServiceMock.Verify(
            x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithNonGenericStore_DoesNotTriggerAutoCreation()
    {
        // Arrange
        var (_, productUrl) = CreatePendingProduct();

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true, Name = "Product", Price = 10.00m, Currency = "USD",
                StoreId = "amazon" // Not generic
            });

        // Act
        await ScrapeNewProductHandler.HandleAsync(
            new ScrapeProductUrlCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        _autoCreateStoreServiceMock.Verify(x => x.AnalyzeHtmlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var storeConfigs = await _dbContext.StoreConfigurations.ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        storeConfigs.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WithGenericStoreButNoHtml_DoesNotTriggerAutoCreation()
    {
        // Arrange
        var (_, productUrl) = CreatePendingProduct();

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true, Name = "Product", Price = 10.00m, Currency = "USD",
                StoreId = "generic", FetchedHtml = null
            });

        // Act
        await ScrapeNewProductHandler.HandleAsync(
            new ScrapeProductUrlCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        _autoCreateStoreServiceMock.Verify(x => x.AnalyzeHtmlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithExistingUserStoreConfig_DoesNotDuplicate()
    {
        // Arrange
        var (_, productUrl) = CreatePendingProduct();
        var html = "<html><body><span class='price'>$10.00</span></body></html>";

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true, Name = "Product", Price = 10.00m, Currency = "USD",
                StoreId = "generic", FetchedHtml = html
            });

        _autoCreateStoreServiceMock
            .Setup(x => x.AnalyzeHtmlAsync(html, productUrl.Url, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutoCreateStoreResult
            {
                StoreName = "Newshop", Domain = "newshop.com",
                Selectors = new StoreSelectorConfig { PriceSelectors = [".price"], NameSelectors = ["h1"], ImageSelectors = [] }
            });

        // User already has a config for this domain
        _configProviderMock
            .Setup(x => x.GetConfigForUrlAsync(productUrl.Url, _testUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoreConfig
            {
                Id = "existing", Name = "Existing", DomainPatterns = ["newshop.com"],
                Selectors = new StoreSelectorConfig { PriceSelectors = [".price"], NameSelectors = [], ImageSelectors = [] },
                IsBuiltIn = false
            });

        // Act
        await ScrapeNewProductHandler.HandleAsync(
            new ScrapeProductUrlCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert - no new store config created
        var storeConfigs = await _dbContext.StoreConfigurations.ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        storeConfigs.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WithBuiltInStoreMatch_AllowsAutoCreation()
    {
        // Arrange
        var (_, productUrl) = CreatePendingProduct();
        var html = "<html><body><span class='price'>$10.00</span></body></html>";

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true, Name = "Product", Price = 10.00m, Currency = "USD",
                StoreId = "generic", FetchedHtml = html
            });

        _autoCreateStoreServiceMock
            .Setup(x => x.AnalyzeHtmlAsync(html, productUrl.Url, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutoCreateStoreResult
            {
                StoreName = "Newshop", Domain = "newshop.com",
                Selectors = new StoreSelectorConfig { PriceSelectors = [".price"], NameSelectors = ["h1"], ImageSelectors = [] }
            });

        // Only a built-in config matches - auto-creation should still proceed
        _configProviderMock
            .Setup(x => x.GetConfigForUrlAsync(productUrl.Url, _testUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoreConfig
            {
                Id = "builtin", Name = "Built-in", DomainPatterns = ["newshop.com"],
                Selectors = new StoreSelectorConfig { PriceSelectors = [], NameSelectors = [], ImageSelectors = [] },
                IsBuiltIn = true
            });

        // Act
        await ScrapeNewProductHandler.HandleAsync(
            new ScrapeProductUrlCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var storeConfigs = await _dbContext.StoreConfigurations.ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        storeConfigs.Should().HaveCount(1);
        storeConfigs[0].IsAutoCreated.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenAnalyzeReturnsNull_DoesNotCreateStore()
    {
        // Arrange
        var (_, productUrl) = CreatePendingProduct();
        var html = "<html><body>No price here</body></html>";

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true, Name = "Product", Price = 10.00m, Currency = "USD",
                StoreId = "generic", FetchedHtml = html
            });

        _autoCreateStoreServiceMock
            .Setup(x => x.AnalyzeHtmlAsync(html, productUrl.Url, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AutoCreateStoreResult?)null);

        // Act
        await ScrapeNewProductHandler.HandleAsync(
            new ScrapeProductUrlCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var storeConfigs = await _dbContext.StoreConfigurations.ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        storeConfigs.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenAutoCreateThrows_StillSavesProduct()
    {
        // Arrange
        var (product, productUrl) = CreatePendingProduct();

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true, Name = "Product", Price = 10.00m, Currency = "USD",
                StoreId = "generic", FetchedHtml = "<html></html>"
            });

        _autoCreateStoreServiceMock
            .Setup(x => x.AnalyzeHtmlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Something went wrong"));

        // Act
        var result = await ScrapeNewProductHandler.HandleAsync(
            new ScrapeProductUrlCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert - product still saved successfully
        result.Should().NotBeNull();
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.Status.Should().Be(ProductStatus.Active);
        updatedProduct.CurrentPrice.Should().Be(10.00m);
    }

    [Fact]
    public async Task HandleAsync_WithFailedScrape_DoesNotTriggerAutoCreation()
    {
        // Arrange
        var (_, productUrl) = CreatePendingProduct();

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScrapingResult.Failure("Could not extract price"));

        // Act
        await ScrapeNewProductHandler.HandleAsync(
            new ScrapeProductUrlCommand(productUrl.Id), _dbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), _loggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        _autoCreateStoreServiceMock.Verify(x => x.AnalyzeHtmlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
