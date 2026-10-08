using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Commands;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;
using Ophi.Worker.Handlers;
using Wolverine;

namespace Ophi.Infrastructure.Tests.Handlers;

/// <summary>
/// Lockdown for the SSE "scrape-completed" ping on the first-time scrape path
/// (<see cref="ScrapeNewProductHandler"/>): publishes a <see cref="LiveUpdate"/> on every outcome
/// (success / OOS / failure), and not on the early no-op returns.
/// </summary>
public class ScrapeNewProductLiveUpdateTests : IDisposable
{
    private readonly Mock<IScrapingService> _scrapingServiceMock = new();
    private readonly Mock<IAutoCreateStoreService> _autoCreateStoreServiceMock = new();
    private readonly Mock<IStoreConfigProvider> _configProviderMock = new();
    private readonly Mock<ILogger> _loggerMock = new();
    private readonly Mock<IMessageBus> _busMock = new();
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Guid _testUserId = Guid.NewGuid();

    public ScrapeNewProductLiveUpdateTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _dbContext = new OphiDbContext(new DbContextOptionsBuilder<OphiDbContext>().UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();
        _dbContext.Users.Add(new User { Id = _testUserId, Email = "t@e.com", Name = "T", PasswordHash = "h" });
        _dbContext.SaveChanges();
    }

    private async Task<(Product product, ProductUrl productUrl)> SeedPendingProductAsync()
    {
        var product = new Product { Id = Guid.NewGuid(), UserId = _testUserId, Name = "Loading...", Currency = "USD", Status = ProductStatus.Pending };
        var productUrl = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://example.com/p", Currency = "USD" };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (product, productUrl);
    }

    private void SetupScrape(ScrapingResult result) => _scrapingServiceMock
        .Setup(x => x.ScrapeProductAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(result);

    private Task InvokeAsync(Guid productUrlId) => ScrapeNewProductHandler.HandleAsync(
        new ScrapeProductUrlCommand(productUrlId), _dbContext, _scrapingServiceMock.Object,
        _autoCreateStoreServiceMock.Object, _configProviderMock.Object, TimeProvider.System,
        _busMock.Object, _loggerMock.Object, TestContext.Current.CancellationToken);

    private void VerifyScrapeCompletedPublished(Guid productId, Times times) =>
        _busMock.Verify(b => b.PublishAsync(
            It.Is<LiveUpdate>(u => u.Kind == LiveUpdate.ScrapeCompleted && u.ProductId == productId && u.UserId == _testUserId),
            It.IsAny<DeliveryOptions>()), times);

    [Fact]
    public async Task HandleAsync_OnSuccessfulScrape_PublishesScrapeCompleted()
    {
        var (product, productUrl) = await SeedPendingProductAsync();
        SetupScrape(new ScrapingResult { Success = true, Name = "P", Price = 9.99m, Currency = "USD" });

        await InvokeAsync(productUrl.Id);

        VerifyScrapeCompletedPublished(product.Id, Times.Once());
    }

    [Fact]
    public async Task HandleAsync_OnOutOfStock_PublishesScrapeCompleted()
    {
        var (product, productUrl) = await SeedPendingProductAsync();
        SetupScrape(new ScrapingResult { Success = true, IsOutOfStock = true, Price = null, ErrorCategory = ScrapeErrorCategory.OutOfStock });

        await InvokeAsync(productUrl.Id);

        VerifyScrapeCompletedPublished(product.Id, Times.Once());
    }

    [Fact]
    public async Task HandleAsync_OnFailedScrape_PublishesScrapeCompleted()
    {
        var (product, productUrl) = await SeedPendingProductAsync();
        SetupScrape(new ScrapingResult { Success = false, Error = "boom" });

        await InvokeAsync(productUrl.Id);

        VerifyScrapeCompletedPublished(product.Id, Times.Once());
    }

    [Fact]
    public async Task HandleAsync_WhenProductUrlMissing_DoesNotPublish()
    {
        await InvokeAsync(Guid.NewGuid());

        _busMock.Verify(b => b.PublishAsync(It.IsAny<LiveUpdate>(), It.IsAny<DeliveryOptions>()), Times.Never());
    }

    [Fact]
    public async Task HandleAsync_WhenProductNotPending_DoesNotPublish()
    {
        var (_, productUrl) = await SeedPendingProductAsync();
        var product = await _dbContext.Products.FirstAsync(TestContext.Current.CancellationToken);
        product.MarkActive();
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await InvokeAsync(productUrl.Id);

        _busMock.Verify(b => b.PublishAsync(It.IsAny<LiveUpdate>(), It.IsAny<DeliveryOptions>()), Times.Never());
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
