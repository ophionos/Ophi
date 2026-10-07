using FluentAssertions;
using Moq;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Tests.Helpers;
using Ophi.TestHelpers;
using Ophi.Infrastructure.Webhooks;
using Ophi.Worker.Handlers;

namespace Ophi.Infrastructure.Tests.Handlers;

public class DispatchPriceChangedWebhookHandlerTests : HandlerTestBase
{
    private readonly Mock<IWebhookDispatchService> _webhookDispatchServiceMock = new();

    [Fact]
    public async Task HandleAsync_WhenPriceActuallyChanges_DispatchesPriceChangedWebhook()
    {
        // Arrange
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        product.CurrentPrice = 75m;
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 75m, "USD");

        // Act
        await DispatchPriceChangedWebhookHandler.HandleAsync(
            @event, DbContext, _webhookDispatchServiceMock.Object, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        _webhookDispatchServiceMock.Verify(
            w => w.DispatchAsync(
                WebhookEvents.PriceChanged,
                product.UserId,
                It.Is<WebhookPayload>(p =>
                    p.ProductId == product.Id &&
                    p.ProductName == "Widget" &&
                    p.OldPrice == 100m &&
                    p.NewPrice == 75m &&
                    p.Currency == "USD"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenPriceUnchanged_SkipsDispatch()
    {
        // Arrange
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // OldPrice == NewPrice = no change
        var @event = new PriceUpdatedEvent(product.Id, 80m, 80m, "USD");

        // Act
        await DispatchPriceChangedWebhookHandler.HandleAsync(
            @event, DbContext, _webhookDispatchServiceMock.Object, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        _webhookDispatchServiceMock.Verify(
            w => w.DispatchAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<WebhookPayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenFirstTimePriceSeen_DispatchesPriceChanged()
    {
        // Arrange — OldPrice is null (first time product has a price)
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, null, 99m, "USD");

        // Act
        await DispatchPriceChangedWebhookHandler.HandleAsync(
            @event, DbContext, _webhookDispatchServiceMock.Object, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        _webhookDispatchServiceMock.Verify(
            w => w.DispatchAsync(
                WebhookEvents.PriceChanged,
                product.UserId,
                It.Is<WebhookPayload>(p => p.OldPrice == null && p.NewPrice == 99m),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenProductMinUnchanged_SkipsDispatch_EvenIfNonMinUrlMoved()
    {
        // Multi-URL case: a non-min URL moved (UrlPrice changed) but the product MIN didn't.
        // The price-changed webhook is about the headline product price; per-URL movement
        // alone must not fire it. Lockdown for the URL-vs-product semantics fix.
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(
            ProductId: product.Id,
            OldPrice: 50m,
            NewPrice: 50m,           // product MIN unchanged
            Currency: "USD",
            ProductUrlId: productUrl.Id,
            UrlPrice: 90m,           // a URL moved, but it isn't the MIN
            UrlCurrency: "USD");

        await DispatchPriceChangedWebhookHandler.HandleAsync(
            @event, DbContext, _webhookDispatchServiceMock.Object, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        _webhookDispatchServiceMock.Verify(
            w => w.DispatchAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<WebhookPayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenProductNotFound_DoesNothing()
    {
        // Arrange — product doesn't exist in DB
        var @event = new PriceUpdatedEvent(Guid.NewGuid(), 80m, 60m, "USD");

        // Act — should not throw
        await DispatchPriceChangedWebhookHandler.HandleAsync(
            @event, DbContext, _webhookDispatchServiceMock.Object, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        _webhookDispatchServiceMock.Verify(
            w => w.DispatchAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<WebhookPayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenDispatchThrows_DoesNotRethrow()
    {
        // Arrange
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _webhookDispatchServiceMock
            .Setup(w => w.DispatchAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<WebhookPayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

        var @event = new PriceUpdatedEvent(product.Id, 100m, 75m, "USD");

        // Act — should not throw even if dispatch fails
        var act = async () => await DispatchPriceChangedWebhookHandler.HandleAsync(
            @event, DbContext, _webhookDispatchServiceMock.Object, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }
}
