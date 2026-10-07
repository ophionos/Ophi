using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Commands;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Webhooks;
using Ophi.Worker.Handlers;
using Ophi.Worker.Settings;
using Wolverine;

namespace Ophi.Infrastructure.Tests.Handlers;

/// <summary>
/// Lockdown for the SSE "scrape-completed" ping: <see cref="CheckProductPriceHandler"/> must publish a
/// <see cref="LiveUpdate"/> once a scrape attempt resolves (success / OOS / failure) and must NOT on the
/// early no-op returns (URL missing / inactive / paused). Without these, deleting the publish line
/// leaves the suite green.
/// </summary>
public class CheckProductPriceLiveUpdateTests : IDisposable
{
    private readonly Mock<IScrapingService> _scrapingServiceMock = new();
    private readonly Mock<ILogger> _loggerMock = new();
    private readonly Mock<IOptions<WorkerSettings>> _workerSettingsMock = new();
    private readonly Mock<IWebhookDispatchService> _webhookDispatchServiceMock = new();
    private readonly Mock<IMessageBus> _busMock = new();
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Guid _testUserId = Guid.NewGuid();

    public CheckProductPriceLiveUpdateTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _dbContext = new OphiDbContext(new DbContextOptionsBuilder<OphiDbContext>().UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();
        _dbContext.Users.Add(new User { Id = _testUserId, Email = "t@e.com", Name = "T", PasswordHash = "h" });
        _dbContext.SaveChanges();
        _workerSettingsMock.Setup(x => x.Value).Returns(new WorkerSettings());
    }

    private async Task<(Product product, ProductUrl productUrl)> SeedActiveProductAsync()
    {
        var product = new Product { Id = Guid.NewGuid(), UserId = _testUserId, Name = "P", Currency = "USD", Status = ProductStatus.Active };
        var productUrl = new ProductUrl { Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://example.com/p", Currency = "USD" };
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (product, productUrl);
    }

    private Task InvokeAsync(Guid productUrlId) => CheckProductPriceHandler.HandleAsync(
        new CheckProductUrlPriceCommand(productUrlId), _dbContext, _scrapingServiceMock.Object,
        _workerSettingsMock.Object, _webhookDispatchServiceMock.Object, TimeProvider.System,
        _busMock.Object, _loggerMock.Object, TestContext.Current.CancellationToken);

    private void VerifyScrapeCompletedPublished(Guid productId, Guid userId, Times times) =>
        _busMock.Verify(b => b.PublishAsync(
            It.Is<LiveUpdate>(u => u.Kind == LiveUpdate.ScrapeCompleted && u.ProductId == productId && u.UserId == userId),
            It.IsAny<DeliveryOptions>()), times);

    [Fact]
    public async Task HandleAsync_OnSuccessfulScrape_PublishesScrapeCompleted()
    {
        var (product, productUrl) = await SeedActiveProductAsync();
        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, Price = 42m, Currency = "USD" });

        await InvokeAsync(productUrl.Id);

        VerifyScrapeCompletedPublished(product.Id, _testUserId, Times.Once());
    }

    [Fact]
    public async Task HandleAsync_OnOutOfStock_PublishesScrapeCompleted()
    {
        var (product, productUrl) = await SeedActiveProductAsync();
        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = true, IsOutOfStock = true, Price = null, ErrorCategory = ScrapeErrorCategory.OutOfStock });

        await InvokeAsync(productUrl.Id);

        VerifyScrapeCompletedPublished(product.Id, _testUserId, Times.Once());
    }

    [Fact]
    public async Task HandleAsync_OnFailedScrape_PublishesScrapeCompleted()
    {
        var (product, productUrl) = await SeedActiveProductAsync();
        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = false, Error = "boom" });

        await InvokeAsync(productUrl.Id);

        VerifyScrapeCompletedPublished(product.Id, _testUserId, Times.Once());
    }

    [Fact]
    public async Task HandleAsync_WhenProductUrlMissing_DoesNotPublish()
    {
        await InvokeAsync(Guid.NewGuid());

        _busMock.Verify(b => b.PublishAsync(It.IsAny<LiveUpdate>(), It.IsAny<DeliveryOptions>()), Times.Never());
    }

    [Fact]
    public async Task HandleAsync_WhenProductInactive_DoesNotPublish()
    {
        var (product, productUrl) = await SeedActiveProductAsync();
        product.Status = ProductStatus.Error;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await InvokeAsync(productUrl.Id);

        _busMock.Verify(b => b.PublishAsync(It.IsAny<LiveUpdate>(), It.IsAny<DeliveryOptions>()), Times.Never());
    }

    [Fact]
    public async Task HandleAsync_WhenUrlPaused_DoesNotPublish()
    {
        var (product, productUrl) = await SeedActiveProductAsync();
        productUrl.Status = ProductUrlStatus.Paused;
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
