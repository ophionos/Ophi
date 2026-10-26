using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Email;
using Ophi.Infrastructure.Tests.Helpers;
using Ophi.Worker.Handlers;

namespace Ophi.Infrastructure.Tests.Handlers;

/// <summary>The basket form of an alert fire (#151): one in-app notification, one message per channel.</summary>
public class SendBasketAlertNotificationHandlerTests : HandlerTestBase
{
    private readonly IOptions<EmailSettings> _email = Options.Create(new EmailSettings { AppUrl = "https://ophi.example" });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Basket Basket, BasketAlert Alert)> Arrange()
    {
        var basket = new Basket { Id = Guid.NewGuid(), UserId = TestUserId, Name = "Desk setup" };
        var alert = new BasketAlert
        {
            Id = Guid.NewGuid(), BasketId = basket.Id, UserId = TestUserId, TargetTotal = 60m, Currency = "EUR", Host = null
        };
        DbContext.Baskets.Add(basket);
        DbContext.BasketAlerts.Add(alert);
        await DbContext.SaveChangesAsync(Ct);
        return (basket, alert);
    }

    private Task<Wolverine.OutgoingMessages> Handle(BasketAlert alert, Basket basket) =>
        SendBasketAlertNotificationHandler.HandleAsync(
            new BasketAlertTriggeredEvent(alert.Id, basket.Id, TestUserId, "amazon.es", 59.83m, 60m, "EUR"),
            DbContext, _email, TimeProvider.System, LoggerMock.Object, Ct);

    [Fact]
    public async Task HandleAsync_CreatesABasketNotificationAndCountsTheFiring()
    {
        var (basket, alert) = await Arrange();

        await Handle(alert, basket);

        var notification = await DbContext.Notifications.SingleAsync(Ct);
        notification.Type.Should().Be(NotificationType.BasketAlert);
        notification.BasketId.Should().Be(basket.Id);
        notification.ProductId.Should().BeNull();
        notification.Title.Should().Contain("Desk setup");
        notification.Message.Should().Contain("amazon.es").And.Contain("59.83").And.Contain("60.00").And.Contain("EUR");
        (await DbContext.BasketAlerts.SingleAsync(Ct)).TriggerCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_EveryEnabledChannel_GetsOneMessageInTheBasketForm()
    {
        TestUser.EmailNotificationsEnabled = true;
        TestUser.DiscordNotificationsEnabled = true;
        TestUser.DiscordWebhookUrl = "https://discord.com/api/webhooks/1/x";
        TestUser.TelegramNotificationsEnabled = true;
        TestUser.TelegramChatId = "42";
        TestUser.PushoverNotificationsEnabled = true;
        TestUser.PushoverUserKey = "ukey";
        TestUser.NtfyNotificationsEnabled = true;
        TestUser.NtfyTopicUrl = "https://ntfy.sh/topic";
        var (basket, alert) = await Arrange();
        var link = $"https://ophi.example/baskets/{basket.Id}";

        var outgoing = (await Handle(alert, basket)).ToList();

        var email = outgoing.OfType<SendAlertEmailRequested>().Single();
        email.Should().BeEquivalentTo(new
        {
            ProductName = "Desk setup", ProductUrl = link, CurrentPrice = 59.83m, TargetPrice = 60m, Currency = "EUR",
            Condition = AlertCondition.Below, Store = "amazon.es"
        });
        outgoing.OfType<SendAlertDiscordRequested>().Single().Store.Should().Be("amazon.es");
        outgoing.OfType<SendAlertTelegramRequested>().Single().Store.Should().Be("amazon.es");
        outgoing.OfType<SendAlertPushoverRequested>().Single().Store.Should().Be("amazon.es");
        outgoing.OfType<SendAlertNtfyRequested>().Single().ProductUrl.Should().Be(link);
        outgoing.OfType<SendBasketAlertWebhookRequested>().Single().Should().BeEquivalentTo(new
        {
            BasketAlertId = alert.Id, UserId = TestUserId, BasketId = basket.Id, BasketName = "Desk setup",
            Store = "amazon.es", Total = 59.83m, TargetTotal = 60m, Currency = "EUR", Url = link
        });
        outgoing.OfType<LiveUpdate>().Single().Kind.Should().Be(LiveUpdate.Notification);
    }

    [Fact]
    public async Task HandleAsync_DeletedAlert_SendsNothing()
    {
        var (basket, alert) = await Arrange();
        DbContext.BasketAlerts.Remove(alert);
        await DbContext.SaveChangesAsync(Ct);

        (await Handle(alert, basket)).Should().BeEmpty();
        (await DbContext.Notifications.CountAsync(Ct)).Should().Be(0);
    }
}
