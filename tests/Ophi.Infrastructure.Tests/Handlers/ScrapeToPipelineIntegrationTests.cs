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
using Ophi.Infrastructure.Scraping.Adapters;
using Ophi.Infrastructure.Settings;
using Ophi.Infrastructure.Tests.Helpers;
using Ophi.TestHelpers;
using Ophi.Infrastructure.Webhooks;
using Ophi.Worker.Handlers;
using Wolverine;

namespace Ophi.Infrastructure.Tests.Handlers;

public class ScrapeToPipelineIntegrationTests : HandlerTestBase
{
    private readonly Mock<IScrapingService> _scrapingServiceMock = new();
    private readonly Mock<IAutoCreateStoreService> _autoCreateStoreServiceMock = new();
    private readonly Mock<IStoreConfigProvider> _configProviderMock = new();
    private readonly Mock<IEmailService> _emailServiceMock = new();
    private readonly Mock<IDiscordService> _discordServiceMock = new();
    private readonly Mock<IWebhookDispatchService> _webhookDispatchServiceMock = new();
    private readonly IOptions<AlertSettings> _alertSettings = Options.Create(new AlertSettings { CooldownMinutes = 60, MaxAlertsPerUser = 100 });

    #region Integration Pipeline Tests

    [Fact]
    public async Task ScrapePipeline_WhenSuccessful_AlertFiresAndNotificationCreated()
    {
        // Arrange — Pending product with an alert
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();

        var product = new Product
        {
            Id = productId,
            UserId = TestUserId,
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
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);

        // Alert: fire if price drops below $50
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Product = product,
            UserId = TestUserId,
            User = TestUser,
            TargetPrice = 50m,
            Condition = AlertCondition.Below,
            IsActive = true
        };
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Scraper returns price $45 (below $50 target)
        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Name = "Test Product",
                Price = 45m,
                Currency = "USD",
                ImageUrl = "https://example.com/image.jpg",
                StoreId = "amazon" // Non-generic to skip auto-store creation
            });

        var command = new ScrapeProductUrlCommand(productUrlId);

        // Act — Scrape → Alert → Notification
        // 1. ScrapeNewProductHandler
        var priceEvent = await ScrapeNewProductHandler.HandleAsync(
            command, DbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), LoggerMock.Object, TestContext.Current.CancellationToken);

        // Verify product was updated with price
        var updatedProduct = await DbContext.Products.FindAsync([productId], TestContext.Current.CancellationToken);
        updatedProduct!.CurrentPrice.Should().Be(45m);

        // 2. CheckAlertsHandler (with updated product price)
        if (priceEvent != null)
        {
            var triggeredEvents = (await CheckAlertsHandler.HandleAsync(
                priceEvent, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken)).ToList();

            // 3. SendAlertNotificationHandler
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
        notification.Title.Should().Contain("Test Product");
    }

    [Fact]
    public async Task ScrapePipeline_WhenSuccessful_PricePointRecorded()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();

        var product = new Product
        {
            Id = productId,
            UserId = TestUserId,
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
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Name = "Test Product",
                Price = 99.99m,
                Currency = "USD",
                StoreId = "amazon"
            });

        var command = new ScrapeProductUrlCommand(productUrlId);

        // Act
        var priceEvent = await ScrapeNewProductHandler.HandleAsync(
            command, DbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), LoggerMock.Object, TestContext.Current.CancellationToken);

        // Record price point
        if (priceEvent != null)
        {
            await RecordPriceHistoryHandler.HandleAsync(
                priceEvent, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);
        }

        // Assert
        var pricePoint = await DbContext.PricePoints.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        pricePoint.Should().NotBeNull();
        pricePoint!.ProductId.Should().Be(productId);
        pricePoint.ProductUrlId.Should().Be(productUrlId);
        pricePoint.Price.Should().Be(99.99m);
        pricePoint.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task ScrapePipeline_WhenOutOfStock_NoAlertFired()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();

        var product = new Product
        {
            Id = productId,
            UserId = TestUserId,
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
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);

        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
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
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                IsOutOfStock = true,
                Name = "Out of Stock Product",
                Currency = "USD",
                StoreId = "amazon"
            });

        var command = new ScrapeProductUrlCommand(productUrlId);

        // Act
        var priceEvent = await ScrapeNewProductHandler.HandleAsync(
            command, DbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), LoggerMock.Object, TestContext.Current.CancellationToken);

        // If OOS, priceEvent will be null
        if (priceEvent != null)
        {
            await RecordPriceHistoryHandler.HandleAsync(
                priceEvent, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

            var triggeredEvents = (await CheckAlertsHandler.HandleAsync(
                priceEvent, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken)).ToList();

            foreach (var triggeredEvent in triggeredEvents)
            {
                await SendAlertNotificationHandler.HandleAsync(
                    triggeredEvent, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);
            }
        }

        // Assert
        // OOS returns null from scraper, so no PriceUpdatedEvent → no alerts fired → no PriceAlert notifications
        var alertNotification = await DbContext.Notifications.FirstOrDefaultAsync(
            n => n.Type == NotificationType.PriceAlert, TestContext.Current.CancellationToken);
        alertNotification.Should().BeNull("OOS should not trigger price alert");

        var pricePoint = await DbContext.PricePoints.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        pricePoint.Should().BeNull("OOS should not record price point from alert pipeline");
    }

    [Fact]
    public async Task ScrapePipeline_WhenScrapeFails_NoAlertFired()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();

        var product = new Product
        {
            Id = productId,
            UserId = TestUserId,
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
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);

        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
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
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult { Success = false, Error = "Connection timeout" });

        var command = new ScrapeProductUrlCommand(productUrlId);

        // Act
        var priceEvent = await ScrapeNewProductHandler.HandleAsync(
            command, DbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), LoggerMock.Object, TestContext.Current.CancellationToken);

        if (priceEvent != null)
        {
            await RecordPriceHistoryHandler.HandleAsync(
                priceEvent, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

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
        notification.Should().BeNull("Scrape failure should not fire alert");

        var updatedProduct = await DbContext.Products.FindAsync([productId], TestContext.Current.CancellationToken);
        updatedProduct!.Status.Should().Be(ProductStatus.Error);
    }

    [Fact]
    public async Task ScrapePipeline_WhenPriceChangedButAlertNotMet_OnlyPricePointRecorded()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var productUrlId = Guid.NewGuid();

        var product = new Product
        {
            Id = productId,
            UserId = TestUserId,
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
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);

        // Alert: fire if price drops below $50
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Product = product,
            UserId = TestUserId,
            User = TestUser,
            TargetPrice = 50m,
            Condition = AlertCondition.Below,
            IsActive = true
        };
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Scraper returns $75 (above $50 threshold — condition not met)
        _scrapingServiceMock
            .Setup(x => x.ScrapeProductAsync(productUrl.Url, null, It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapingResult
            {
                Success = true,
                Name = "Test Product",
                Price = 75m,
                Currency = "USD",
                StoreId = "amazon"
            });

        var command = new ScrapeProductUrlCommand(productUrlId);

        // Act
        var priceEvent = await ScrapeNewProductHandler.HandleAsync(
            command, DbContext, _scrapingServiceMock.Object,
            _autoCreateStoreServiceMock.Object, _configProviderMock.Object,
            TimeProvider.System, Mock.Of<IMessageBus>(), LoggerMock.Object, TestContext.Current.CancellationToken);

        if (priceEvent != null)
        {
            await RecordPriceHistoryHandler.HandleAsync(
                priceEvent, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

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
        notification.Should().BeNull("Price above target should not fire alert");

        var pricePoint = await DbContext.PricePoints.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        pricePoint.Should().NotBeNull("Price change should be recorded even if alert not triggered");
        pricePoint!.Price.Should().Be(75m);
    }

    #endregion
}
