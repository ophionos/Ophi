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
    // A basket alert (#151) is told apart by its store; every channel must pass it to the renderer.
    [Fact]
    public async Task EveryPushChannel_PassesTheStoreThrough()
    {
        var telegram = new Mock<ITelegramService>();
        var pushover = new Mock<IPushoverService>();
        var ntfy = new Mock<INtfyService>();
        var ct = TestContext.Current.CancellationToken;

        await SendAlertTelegramHandler.HandleAsync(
            new SendAlertTelegramRequested(Guid.NewGuid(), "42", "Camping", "https://x", 45m, 50m, "EUR", AlertCondition.Below, Store: "shop-a.es"),
            telegram.Object, NullLogger.Instance, ct);
        await SendAlertPushoverHandler.HandleAsync(
            new SendAlertPushoverRequested(Guid.NewGuid(), "user", "Camping", "https://x", 45m, 50m, "EUR", AlertCondition.Below, Store: "shop-a.es"),
            pushover.Object, NullLogger.Instance, ct);
        await SendAlertNtfyHandler.HandleAsync(
            new SendAlertNtfyRequested(Guid.NewGuid(), "https://ntfy.sh/t", "Camping", "https://x", 45m, 50m, "EUR", AlertCondition.Below, Store: "shop-a.es"),
            ntfy.Object, NullLogger.Instance, ct);

        telegram.Verify(t => t.SendPriceAlertAsync(It.Is<PushPriceAlert>(a => a.Store == "shop-a.es"), "42", It.IsAny<CancellationToken>()), Times.Once);
        pushover.Verify(p => p.SendPriceAlertAsync(It.Is<PushPriceAlert>(a => a.Store == "shop-a.es"), "user", It.IsAny<CancellationToken>()), Times.Once);
        ntfy.Verify(n => n.SendPriceAlertAsync(It.Is<PushPriceAlert>(a => a.Store == "shop-a.es"), "https://ntfy.sh/t", It.IsAny<CancellationToken>()), Times.Once);
    }

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

    [Fact]
    public async Task Ntfy_ForwardsTheAlertToTheTopicUrl()
    {
        var ntfy = new Mock<INtfyService>();

        await SendAlertNtfyHandler.HandleAsync(
            new SendAlertNtfyRequested(Guid.NewGuid(), "https://ntfy.sh/t", "Widget", "https://x", 45m, 20m, "USD", AlertCondition.PercentDrop),
            ntfy.Object, NullLogger.Instance, TestContext.Current.CancellationToken);

        ntfy.Verify(n => n.SendPriceAlertAsync(
            It.Is<PushPriceAlert>(a => a.ProductName == "Widget" && a.Condition == AlertCondition.PercentDrop),
            "https://ntfy.sh/t", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ntfy_WhenSendFails_Propagates_ForWolverineRetry()
    {
        var ntfy = new Mock<INtfyService>();
        ntfy.Setup(n => n.SendPriceAlertAsync(It.IsAny<PushPriceAlert>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("403"));

        var act = () => SendAlertNtfyHandler.HandleAsync(
            new SendAlertNtfyRequested(Guid.NewGuid(), "https://ntfy.sh/t", "Widget", "", 45m, 50m, "USD"),
            ntfy.Object, NullLogger.Instance, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
