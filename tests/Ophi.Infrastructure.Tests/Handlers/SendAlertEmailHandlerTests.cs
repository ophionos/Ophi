using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Email;
using Ophi.Worker.Handlers;

namespace Ophi.Infrastructure.Tests.Handlers;

public class SendAlertEmailHandlerTests
{
    private readonly Mock<IEmailService> _emailServiceMock = new();

    [Fact]
    public async Task HandleAsync_SendsPriceAlertEmail()
    {
        var @event = new SendAlertEmailRequested(
            AlertId: Guid.NewGuid(),
            RecipientEmail: "alice@example.com",
            RecipientName: "Alice",
            ProductName: "Widget",
            ProductUrl: "https://example.com/widget",
            CurrentPrice: 45m,
            TargetPrice: 50m,
            Currency: "USD");

        await SendAlertEmailHandler.HandleAsync(
            @event, _emailServiceMock.Object, NullLogger.Instance, TestContext.Current.CancellationToken);

        _emailServiceMock.Verify(
            e => e.SendPriceAlertAsync(
                It.Is<PriceAlertEmail>(p =>
                    p.ToEmail == "alice@example.com" &&
                    p.ToName == "Alice" &&
                    p.ProductName == "Widget" &&
                    p.ProductUrl == "https://example.com/widget" &&
                    p.CurrentPrice == 45m &&
                    p.TargetPrice == 50m &&
                    p.Currency == "USD"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenEmailServiceThrows_PropagatesExceptionForWolverineRetry()
    {
        _emailServiceMock
            .Setup(e => e.SendPriceAlertAsync(It.IsAny<PriceAlertEmail>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("SMTP unavailable"));

        var @event = new SendAlertEmailRequested(
            AlertId: Guid.NewGuid(),
            RecipientEmail: "alice@example.com",
            RecipientName: "Alice",
            ProductName: "Widget",
            ProductUrl: "https://example.com/widget",
            CurrentPrice: 45m,
            TargetPrice: 50m,
            Currency: "USD");

        var act = async () => await SendAlertEmailHandler.HandleAsync(
            @event, _emailServiceMock.Object, NullLogger.Instance, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<HttpRequestException>(act);
    }
}
