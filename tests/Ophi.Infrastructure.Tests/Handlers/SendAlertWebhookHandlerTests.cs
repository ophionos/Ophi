using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Webhooks;
using Ophi.Worker.Handlers;

namespace Ophi.Infrastructure.Tests.Handlers;

public class SendAlertWebhookHandlerTests
{
    private readonly Mock<IWebhookDispatchService> _webhookDispatchMock = new();

    [Fact]
    public async Task HandleAsync_DispatchesAlertFiredWebhookForUser()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var @event = new SendAlertWebhookRequested(
            AlertId: Guid.NewGuid(),
            UserId: userId,
            ProductId: productId,
            ProductName: "Widget",
            ProductUrl: "https://example.com/widget",
            OldPrice: 60m,
            CurrentPrice: 45m,
            Currency: "USD");

        await SendAlertWebhookHandler.HandleAsync(
            @event, _webhookDispatchMock.Object, TimeProvider.System, NullLogger.Instance, TestContext.Current.CancellationToken);

        _webhookDispatchMock.Verify(
            w => w.DispatchAsync(
                WebhookEvents.AlertFired,
                userId,
                It.Is<WebhookPayload>(p =>
                    p.ProductId == productId &&
                    p.ProductName == "Widget" &&
                    p.ProductUrl == "https://example.com/widget" &&
                    p.OldPrice == 60m &&
                    p.NewPrice == 45m &&
                    p.Currency == "USD"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithNoOldPrice_DispatchesWithNullOldPrice()
    {
        var @event = new SendAlertWebhookRequested(
            AlertId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            ProductId: Guid.NewGuid(),
            ProductName: "Widget",
            ProductUrl: "https://example.com/widget",
            OldPrice: null,
            CurrentPrice: 45m,
            Currency: "USD");

        await SendAlertWebhookHandler.HandleAsync(
            @event, _webhookDispatchMock.Object, TimeProvider.System, NullLogger.Instance, TestContext.Current.CancellationToken);

        _webhookDispatchMock.Verify(
            w => w.DispatchAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.Is<WebhookPayload>(p => p.OldPrice == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenWebhookDispatchThrows_PropagatesExceptionForWolverineRetry()
    {
        _webhookDispatchMock
            .Setup(w => w.DispatchAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<WebhookPayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Downstream 500"));

        var @event = new SendAlertWebhookRequested(
            AlertId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            ProductId: Guid.NewGuid(),
            ProductName: "Widget",
            ProductUrl: "https://example.com/widget",
            OldPrice: 60m,
            CurrentPrice: 45m,
            Currency: "USD");

        var act = async () => await SendAlertWebhookHandler.HandleAsync(
            @event, _webhookDispatchMock.Object, TimeProvider.System, NullLogger.Instance, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<HttpRequestException>(act);
    }
}
