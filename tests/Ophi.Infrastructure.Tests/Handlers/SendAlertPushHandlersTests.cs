using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Push;
using Ophi.Worker.Handlers;

namespace Ophi.Infrastructure.Tests.Handlers;

public class SendAlertPushHandlersTests
{
    [Fact]
    public async Task Telegram_ForwardsTheAlertToTheChat()
    {
        var telegram = new Mock<ITelegramService>();

        await SendAlertTelegramHandler.HandleAsync(
            new SendAlertTelegramRequested(Guid.NewGuid(), "42", "Widget", "https://x", 45m, 20m, "USD", AlertCondition.PercentDrop),
            telegram.Object, NullLogger.Instance, TestContext.Current.CancellationToken);

        telegram.Verify(t => t.SendPriceAlertAsync(
            It.Is<PushPriceAlert>(a => a.ProductName == "Widget" && a.TargetPrice == 20m && a.Condition == AlertCondition.PercentDrop),
            "42", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Telegram_WhenSendFails_Propagates_ForWolverineRetry()
    {
        var telegram = new Mock<ITelegramService>();
        telegram.Setup(t => t.SendPriceAlertAsync(It.IsAny<PushPriceAlert>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503"));

        var act = () => SendAlertTelegramHandler.HandleAsync(
            new SendAlertTelegramRequested(Guid.NewGuid(), "42", "Widget", "", 45m, 50m, "USD"),
            telegram.Object, NullLogger.Instance, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Pushover_ForwardsTheAlertToTheUserKey()
    {
        var pushover = new Mock<IPushoverService>();

        await SendAlertPushoverHandler.HandleAsync(
            new SendAlertPushoverRequested(Guid.NewGuid(), "ukey", "Widget", "https://x", 45m, 50m, "USD", AlertCondition.Below),
            pushover.Object, NullLogger.Instance, TestContext.Current.CancellationToken);

        pushover.Verify(p => p.SendPriceAlertAsync(
            It.Is<PushPriceAlert>(a => a.CurrentPrice == 45m && a.Currency == "USD"),
            "ukey", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Pushover_WhenSendFails_Propagates_ForWolverineRetry()
    {
        var pushover = new Mock<IPushoverService>();
        pushover.Setup(p => p.SendPriceAlertAsync(It.IsAny<PushPriceAlert>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("400"));

        var act = () => SendAlertPushoverHandler.HandleAsync(
            new SendAlertPushoverRequested(Guid.NewGuid(), "ukey", "Widget", "", 45m, 50m, "USD"),
            pushover.Object, NullLogger.Instance, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
