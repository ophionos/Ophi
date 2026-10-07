using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Commands;
using Ophi.Infrastructure.Discord;
using Ophi.Infrastructure.Email;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Settings;
using Ophi.Infrastructure.Tests.Helpers;
using Ophi.TestHelpers;
using Ophi.Infrastructure.Webhooks;
using Ophi.Worker.Handlers;
using Ophi.Worker.Settings;
using Wolverine;

namespace Ophi.Infrastructure.Tests.Handlers;

public class CheckProductPricePipelineTests : HandlerTestBase
{
    private readonly Mock<IScrapingService> _scrapingServiceMock = new();
    private readonly Mock<IEmailService> _emailServiceMock = new();
    private readonly Mock<IDiscordService> _discordServiceMock = new();
    private readonly Mock<IWebhookDispatchService> _webhookDispatchServiceMock = new();
    private readonly IOptions<WorkerSettings> _workerSettings = Options.Create(new WorkerSettings());
    private readonly IOptions<AlertSettings> _alertSettings = Options.Create(new AlertSettings { CooldownMinutes = 60, MaxAlertsPerUser = 100 });

    [Fact]
    public async Task PriceCheckPipeline_WhenPriceDropsBelow_AlertFiresAndNotificationCreated()
    {
        // Arrange
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = TestUserId,
            Name = "Test Product",
            CurrentPrice = 80m,
            Currency = "USD",
            Status = ProductStatus.Active
        };
        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD",
            CurrentPrice = 80m
        };
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);

        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Product = product,
            UserId = TestUserId,
            User = TestUser,
            TargetPrice = 50m,
            Condition = AlertCondition.Below,
            IsActive = true
        };
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Name = "Test Product",
                Price = 45m,
                Currency = "USD",
                StoreId = "amazon"
            });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act — Simulate pipeline: CheckProductPriceHandler → CheckAlertsHandler → SendAlertNotificationHandler
        var priceEvent = await CheckProductPriceHandler.HandleAsync(
            command, DbContext, _scrapingServiceMock.Object, _workerSettings,
            _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), LoggerMock.Object, TestContext.Current.CancellationToken);

        if (priceEvent != null)
        {
            var triggeredEvents = (await CheckAlertsHandler.HandleAsync(
                priceEvent, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken)).ToList();

            foreach (var triggeredEvent in triggeredEvents)
            {
                await SendAlertNotificationHandler.HandleAsync(
                    triggeredEvent, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);
            }
        }

        // Assert
        var notification = await DbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification.Should().NotBeNull();
        notification!.Type.Should().Be(NotificationType.PriceAlert);
        notification.UserId.Should().Be(TestUserId);
        notification.ProductId.Should().Be(product.Id);
    }

    [Fact]
    public async Task PriceCheckPipeline_WhenPriceDropsBelow_PricePointRecorded()
    {
        // Arrange
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = TestUserId,
            Name = "Test Product",
            CurrentPrice = 80m,
            Currency = "USD",
            Status = ProductStatus.Active
        };
        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD",
            CurrentPrice = 80m
        };
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Name = "Test Product",
                Price = 45m,
                Currency = "USD",
                StoreId = "amazon"
            });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        var priceEvent = await CheckProductPriceHandler.HandleAsync(
            command, DbContext, _scrapingServiceMock.Object, _workerSettings,
            _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), LoggerMock.Object, TestContext.Current.CancellationToken);

        if (priceEvent != null)
        {
            await RecordPriceHistoryHandler.HandleAsync(
                priceEvent, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);
        }

        // Assert
        var pricePoint = await DbContext.PricePoints.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        pricePoint.Should().NotBeNull();
        pricePoint!.Price.Should().Be(45m);
        pricePoint.ProductUrlId.Should().Be(productUrl.Id);
        pricePoint.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task PriceCheckPipeline_WhenUrlIsPaused_SkipsCheckEntirely()
    {
        // Arrange
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = TestUserId,
            Name = "Test Product",
            CurrentPrice = 80m,
            Currency = "USD",
            Status = ProductStatus.Active
        };
        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD",
            CurrentPrice = 80m,
            Status = ProductUrlStatus.Paused
        };
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        var priceEvent = await CheckProductPriceHandler.HandleAsync(
            command, DbContext, _scrapingServiceMock.Object, _workerSettings,
            _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        priceEvent.Should().BeNull("Paused URL should skip check");
        _scrapingServiceMock.Verify(
            x => x.ScrapeProductAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never, "Scraping service should never be called for paused URL");

        var scrapeLog = await DbContext.ScrapeLogs.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        scrapeLog.Should().BeNull("No scrape log should be created for paused URL");
    }

    [Fact]
    public async Task PriceCheckPipeline_WhenProductNotActive_SkipsCheck()
    {
        // Arrange
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = TestUserId,
            Name = "Test Product",
            Currency = "USD",
            Status = ProductStatus.Pending
        };
        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD"
        };
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        var priceEvent = await CheckProductPriceHandler.HandleAsync(
            command, DbContext, _scrapingServiceMock.Object, _workerSettings,
            _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        priceEvent.Should().BeNull("Non-active product should skip check");
        _scrapingServiceMock.Verify(
            x => x.ScrapeProductAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never, "Scraping service should never be called for non-active product");
    }

    [Fact]
    public async Task PriceCheckPipeline_WhenFailureReachesThreshold_DispatchesScrapeFailedWebhook()
    {
        // Arrange
        var settings = new WorkerSettings { MaxFailuresBeforeError = 3 };
        var workerSettings = Options.Create(settings);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = TestUserId,
            Name = "Test Product",
            CurrentPrice = 80m,
            Currency = "USD",
            Status = ProductStatus.Active
        };
        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD",
            CurrentPrice = 80m,
            FailureCount = 2 // One more failure will hit the threshold
        };
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, productUrl.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = false, Error = "Connection timeout" });

        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        // Act
        await CheckProductPriceHandler.HandleAsync(
            command, DbContext, _scrapingServiceMock.Object, workerSettings,
            _webhookDispatchServiceMock.Object, TimeProvider.System, Mock.Of<IMessageBus>(), LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert — webhook dispatched with scrape_failed event
        _webhookDispatchServiceMock.Verify(
            w => w.DispatchAsync(
                WebhookEvents.ScrapeFailed,
                product.UserId,
                It.Is<WebhookPayload>(p => p.ProductId == product.Id && p.ProductName == "Test Product"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PriceCheckPipeline_AntiBotUrlAlongsideAHealthyOne_IsPausedAtThreshold()
    {
        // The case that actually leaked. A product only goes Error when EVERY url is at the
        // threshold, and PriceCheckDispatcher stops dispatching on Product.Status != Active — so a
        // single-url product self-limits. With a healthy sibling holding the product Active, a
        // blocked url was re-scraped every cycle forever, launching Chromium each time for a
        // request that cannot succeed from this host. See issue #136.
        var (product, blockedUrl) = SeedProductWithHealthySibling();
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        StubScrape(blockedUrl, ScrapeErrorCategory.AntiBot, "Blocked by anti-bot protection");
        await RunChecksAsync(blockedUrl, times: 3);

        var saved = await DbContext.ProductUrls.FirstAsync(
            u => u.Id == blockedUrl.Id, TestContext.Current.CancellationToken);
        saved.Status.Should().Be(ProductUrlStatus.Paused);

        var savedProduct = await DbContext.Products.FirstAsync(
            p => p.Id == product.Id, TestContext.Current.CancellationToken);
        savedProduct.Status.Should().Be(ProductStatus.Active,
            "the healthy sibling keeps the product live — which is exactly why the url itself had to be paused");
    }

    [Fact]
    public async Task PriceCheckPipeline_AntiBotPause_NotifiesTheUser()
    {
        var (_, blockedUrl) = SeedProductWithHealthySibling();
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        StubScrape(blockedUrl, ScrapeErrorCategory.AntiBot, "Blocked by anti-bot protection");
        await RunChecksAsync(blockedUrl, times: 3);

        // Exactly one notification: pausing a url silently is the failure mode the '>=' + latch
        // comment in CheckProductPriceHandler exists to prevent, and two at once is just noise.
        var notifications = await DbContext.Notifications.ToListAsync(TestContext.Current.CancellationToken);
        notifications.Should().HaveCount(1);
        notifications[0].Type.Should().Be(NotificationType.UrlHealth);
        notifications[0].Message.Should().ContainEquivalentOf("resume",
            "the operator's way back is to resume the url if their network changes");
    }

    [Fact]
    public async Task PriceCheckPipeline_TransientFailuresAtThreshold_DoNotPauseTheUrl()
    {
        // Category-gated on purpose. A network error may well clear on its own, so it keeps the old
        // behaviour; without this test a later change could widen the pause to every failure and
        // nothing would fail.
        var (_, failingUrl) = SeedProductWithHealthySibling();
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        StubScrape(failingUrl, ScrapeErrorCategory.NetworkError, "Connection timeout");
        await RunChecksAsync(failingUrl, times: 3);

        var saved = await DbContext.ProductUrls.FirstAsync(
            u => u.Id == failingUrl.Id, TestContext.Current.CancellationToken);
        saved.Status.Should().Be(ProductUrlStatus.Active);
        saved.FailureCount.Should().Be(3, "it still counts against the auto-pause budget as before");
    }

    [Fact]
    public async Task PriceCheckPipeline_UrlAlreadyNotifiedForAnotherFailure_StillNotifiesWhenBlocked()
    {
        // The latch divergence: the generic threshold notification fires once per streak, so a url
        // that already failed its way past the threshold on network errors has FailureNotified set.
        // If the site then starts serving a challenge, the pause must still announce itself rather
        // than riding a latch that has already been spent.
        var (_, url) = SeedProductWithHealthySibling();
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        StubScrape(url, ScrapeErrorCategory.NetworkError, "Connection timeout");
        await RunChecksAsync(url, times: 3);
        var beforeBlock = await DbContext.Notifications.CountAsync(TestContext.Current.CancellationToken);

        StubScrape(url, ScrapeErrorCategory.AntiBot, "Blocked by anti-bot protection");
        await RunChecksAsync(url, times: 1);

        var saved = await DbContext.ProductUrls.FirstAsync(
            u => u.Id == url.Id, TestContext.Current.CancellationToken);
        saved.Status.Should().Be(ProductUrlStatus.Paused);

        var afterBlock = await DbContext.Notifications.CountAsync(TestContext.Current.CancellationToken);
        afterBlock.Should().BeGreaterThan(beforeBlock, "a silently paused url is the bug, not the fix");
    }

    /// <summary>
    /// A two-url product: one url that will fail, plus a healthy sibling whose zero failure count
    /// keeps the product Active. That sibling is the whole point — without it the product reaches
    /// the all-urls-failing test, goes Error, and the dispatcher stops on its own.
    /// </summary>
    private (Product Product, ProductUrl FailingUrl) SeedProductWithHealthySibling()
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = TestUserId,
            Name = "Test Product",
            CurrentPrice = 80m,
            Currency = "USD",
            Status = ProductStatus.Active
        };
        var failingUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://blocked.example.com/product",
            Currency = "USD"
        };
        var healthyUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://working.example.com/product",
            Currency = "USD",
            CurrentPrice = 80m
        };
        DbContext.Products.Add(product);
        DbContext.ProductUrls.AddRange(failingUrl, healthyUrl);
        return (product, failingUrl);
    }

    private void StubScrape(ProductUrl url, ScrapeErrorCategory category, string error) =>
        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(url.Url, url.Selector, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = false, Error = error, ErrorCategory = category });

    private async Task RunChecksAsync(ProductUrl url, int times)
    {
        for (var i = 0; i < times; i++)
        {
            await CheckProductPriceHandler.HandleAsync(
                new CheckProductUrlPriceCommand(url.Id), DbContext, _scrapingServiceMock.Object,
                _workerSettings, _webhookDispatchServiceMock.Object, TimeProvider.System,
                Mock.Of<IMessageBus>(), LoggerMock.Object, TestContext.Current.CancellationToken);
        }
    }
}
