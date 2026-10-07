using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Discord;
using Ophi.Worker.Handlers;

namespace Ophi.Infrastructure.Tests.Handlers;

public class SendAlertDiscordHandlerTests
{
    private readonly Mock<IDiscordService> _discordServiceMock = new();

    [Fact]
    public async Task HandleAsync_SendsDiscordWebhookToConfiguredUrl()
    {
        var @event = new SendAlertDiscordRequested(
            AlertId: Guid.NewGuid(),
            DiscordWebhookUrl: "https://discord.com/api/webhooks/abc/def",
            ProductName: "Widget",
            ProductUrl: "https://example.com/widget",
            CurrentPrice: 45m,
            TargetPrice: 50m,
            Currency: "USD");

        await SendAlertDiscordHandler.HandleAsync(
            @event, _discordServiceMock.Object, NullLogger.Instance, TestContext.Current.CancellationToken);

        _discordServiceMock.Verify(
            d => d.SendPriceAlertAsync(
                It.Is<DiscordPriceAlert>(a =>
                    a.ProductName == "Widget" &&
                    a.ProductUrl == "https://example.com/widget" &&
                    a.CurrentPrice == 45m &&
                    a.TargetPrice == 50m &&
                    a.Currency == "USD"),
                "https://discord.com/api/webhooks/abc/def",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenDiscordServiceThrows_PropagatesExceptionForWolverineRetry()
    {
        _discordServiceMock
            .Setup(d => d.SendPriceAlertAsync(It.IsAny<DiscordPriceAlert>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Discord 503"));

        var @event = new SendAlertDiscordRequested(
            AlertId: Guid.NewGuid(),
            DiscordWebhookUrl: "https://discord.com/api/webhooks/abc/def",
            ProductName: "Widget",
            ProductUrl: "https://example.com/widget",
            CurrentPrice: 45m,
            TargetPrice: 50m,
            Currency: "USD");

        var act = async () => await SendAlertDiscordHandler.HandleAsync(
            @event, _discordServiceMock.Object, NullLogger.Instance, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<HttpRequestException>(act);
    }
}
