using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Settings;
using Ophi.Infrastructure.Tests.Helpers;
using Ophi.TestHelpers;
using Ophi.Worker.Handlers;

namespace Ophi.Infrastructure.Tests.Handlers;

public class AlertPipelineIntegrationTests : HandlerTestBase
{
    private readonly IOptions<AlertSettings> _alertSettings = Options.Create(new AlertSettings { CooldownMinutes = 60, MaxAlertsPerUser = 100 });

    #region Integration Pipeline Tests

    [Fact]
    public async Task AlertPipeline_WhenPriceDropsBelow_CreatesNotificationInDb()
    {
        // Arrange
        var product = CreateProduct("Test Product", 40m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var priceEvent = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Act — simulate Wolverine message routing:
        // 1. PriceUpdatedEvent → CheckAlertsHandler → AlertTriggeredEvent[]
        var triggeredEvents = (await CheckAlertsHandler.HandleAsync(
            priceEvent, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken)).ToList();

        // 2. For each AlertTriggeredEvent → SendAlertNotificationHandler
        foreach (var triggeredEvent in triggeredEvents)
        {
            await SendAlertNotificationHandler.HandleAsync(
                triggeredEvent, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);
        }

        // Assert
        var notification = await DbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification.Should().NotBeNull();
        notification!.Type.Should().Be(NotificationType.PriceAlert);
        notification.UserId.Should().Be(TestUserId);
        notification.ProductId.Should().Be(product.Id);
        notification.IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task AlertPipeline_WhenPriceAboveTarget_NoNotificationCreated()
    {
        // Arrange
        var product = CreateProduct("Test Product", 75m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var priceEvent = new PriceUpdatedEvent(product.Id, 100m, 75m, "USD");

        // Act
        var triggeredEvents = (await CheckAlertsHandler.HandleAsync(
            priceEvent, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken)).ToList();

        foreach (var triggeredEvent in triggeredEvents)
        {
            await SendAlertNotificationHandler.HandleAsync(
                triggeredEvent, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);
        }

        // Assert
        var notification = await DbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification.Should().BeNull();
    }

    [Fact]
    public async Task AlertPipeline_WithMultipleAlerts_CreatesOneNotificationPerAlert()
    {
        // Arrange
        var product = CreateProduct("Test Product", 40m, 100m);
        DbContext.Products.Add(product);

        var alert1 = CreateAlert(product, 50m, AlertCondition.Below); // Should trigger
        var alert2 = CreateAlert(product, 45m, AlertCondition.Below); // Should also trigger
        DbContext.Alerts.AddRange(alert1, alert2);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var priceEvent = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Act
        var triggeredEvents = (await CheckAlertsHandler.HandleAsync(
            priceEvent, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken)).ToList();

        foreach (var triggeredEvent in triggeredEvents)
        {
            await SendAlertNotificationHandler.HandleAsync(
                triggeredEvent, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);
        }

        // Assert
        var notifications = await DbContext.Notifications.ToListAsync(TestContext.Current.CancellationToken);
        notifications.Should().HaveCount(2);
        notifications.Should().AllSatisfy(n => n.Type.Should().Be(NotificationType.PriceAlert));
    }

    [Fact]
    public async Task AlertPipeline_WhenAlertInCooldown_NoNotificationCreated()
    {
        // Arrange
        var product = CreateProduct("Test Product", 40m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below, lastTriggeredAt: DateTime.UtcNow.AddMinutes(-30)); // 30 min ago, within 60-min cooldown
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var priceEvent = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Act
        var triggeredEvents = (await CheckAlertsHandler.HandleAsync(
            priceEvent, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken)).ToList();

        foreach (var triggeredEvent in triggeredEvents)
        {
            await SendAlertNotificationHandler.HandleAsync(
                triggeredEvent, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);
        }

        // Assert
        var notification = await DbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification.Should().BeNull();
    }

    [Fact]
    public async Task AlertPipeline_NotificationContainsCorrectPriceAndProduct()
    {
        // Arrange
        var product = CreateProduct("Premium Widget", 45m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var priceEvent = new PriceUpdatedEvent(product.Id, 100m, 45m, "USD");

        // Act
        var triggeredEvents = (await CheckAlertsHandler.HandleAsync(
            priceEvent, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken)).ToList();

        foreach (var triggeredEvent in triggeredEvents)
        {
            await SendAlertNotificationHandler.HandleAsync(
                triggeredEvent, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);
        }

        // Assert
        var notification = await DbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification.Should().NotBeNull();
        notification!.Title.Should().Contain("Premium Widget");
        notification.Message.Should().Contain("45.00");
        notification.Message.Should().Contain("50.00");
        notification.Message.Should().Contain("USD");
    }

    #endregion

    #region Helpers

    private Product CreateProduct(string name, decimal? currentPrice, decimal? previousPrice)
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            UserId = TestUserId,
            Name = name,
            CurrentPrice = currentPrice,
            PreviousPrice = previousPrice,
            Currency = "USD",
            Status = ProductStatus.Active
        };
    }

    private Alert CreateAlert(
        Product product, decimal targetPrice, AlertCondition condition, DateTime? lastTriggeredAt = null)
    {
        return new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Product = product,
            UserId = TestUserId,
            User = TestUser,
            TargetPrice = targetPrice,
            Condition = condition,
            IsActive = true,
            TriggerCount = 0,
            LastTriggeredAt = lastTriggeredAt
        };
    }

    #endregion
}
