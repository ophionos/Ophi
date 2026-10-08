using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Tests.Helpers;
using Ophi.TestHelpers;
using Ophi.Worker.Handlers;

namespace Ophi.Infrastructure.Tests.Handlers;

public class SendAlertNotificationHandlerTests : HandlerTestBase
{
    [Fact]
    public async Task HandleAsync_CreatesNotificationWithPriceAlertType()
    {
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 50m);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD");

        await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        var notification = await DbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification.Should().NotBeNull();
        notification.Type.Should().Be(NotificationType.PriceAlert);
        notification.UserId.Should().Be(TestUserId);
        notification.ProductId.Should().Be(product.Id);
        notification.IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_NotificationTitleContainsProductName()
    {
        var (product, productUrl) = TestEntityFactory.CreateProduct("Super Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 50m);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD");

        await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        var notification = await DbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification!.Title.Should().Contain("Super Widget");
    }

    [Fact]
    public async Task HandleAsync_NotificationMessageContainsPriceDetails()
    {
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 50m);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD");

        await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        var notification = await DbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification!.Message.Should().Contain("45");
        notification.Message.Should().Contain("50");
        notification.Message.Should().Contain("USD");
    }

    [Fact]
    public async Task HandleAsync_CascadesEmailRequest()
    {
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 50m);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD");

        var outgoing = await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        var emailRequest = outgoing.OfType<SendAlertEmailRequested>().FirstOrDefault();
        emailRequest.Should().NotBeNull();
        emailRequest!.AlertId.Should().Be(alert.Id);
        emailRequest.RecipientEmail.Should().Be(TestUser.Email);
        emailRequest.RecipientName.Should().Be(TestUser.Name);
        emailRequest.ProductName.Should().Be("Widget");
        emailRequest.CurrentPrice.Should().Be(45m);
        emailRequest.TargetPrice.Should().Be(50m);
        emailRequest.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task HandleAsync_PropagatesTheAlertConditionToEveryRenderingChannel()
    {
        // TargetPrice alone is ambiguous — money for Below/Above, a percentage for PercentDrop. The
        // orchestrator is the only place that has the Alert entity, so if it drops Condition here the
        // channel templates have no way to tell and fall back to rendering a percentage as money.
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.Alert(product.Id, TestUserId)
            .WithTarget(20m).WithCondition(AlertCondition.PercentDrop).Build();
        TestUser.DiscordNotificationsEnabled = true;
        TestUser.DiscordWebhookUrl = "https://discord.com/api/webhooks/1/x";
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 20m, "USD");

        var outgoing = await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        outgoing.OfType<SendAlertEmailRequested>().Single()
            .Condition.Should().Be(AlertCondition.PercentDrop);
        outgoing.OfType<SendAlertDiscordRequested>().Single()
            .Condition.Should().Be(AlertCondition.PercentDrop);
    }

    [Fact]
    public async Task HandleAsync_PercentDropNotification_DescribesAPercentageNotAnAmount()
    {
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.Alert(product.Id, TestUserId)
            .WithTarget(20m).WithCondition(AlertCondition.PercentDrop).Build();
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 20m, "USD");

        await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        var notification = await DbContext.Notifications.FirstAsync(TestContext.Current.CancellationToken);
        notification.Message.Should().Contain("20%");
        notification.Message.Should().NotContain("USD 20.00");
    }

    [Fact]
    public async Task HandleAsync_CascadesLiveUpdateNotificationPing()
    {
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 50m);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD");

        var outgoing = await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        var ping = outgoing.OfType<LiveUpdate>().FirstOrDefault();
        ping.Should().NotBeNull();
        ping!.Kind.Should().Be(LiveUpdate.Notification);
        ping.UserId.Should().Be(TestUserId);
        ping.ProductId.Should().Be(product.Id);
    }

    [Fact]
    public async Task HandleAsync_WithMissingAlert_DoesNotCreateNotificationAndCascadesNothing()
    {
        var @event = new AlertTriggeredEvent(Guid.NewGuid(), Guid.NewGuid(), TestUserId, 45m, 50m, "USD");

        var outgoing = await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        var count = await DbContext.Notifications.CountAsync(TestContext.Current.CancellationToken);
        count.Should().Be(0);
        outgoing.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenUserHasDiscordEnabled_CascadesDiscordRequest()
    {
        var user = DbContext.Users.First(u => u.Id == TestUserId);
        user.DiscordNotificationsEnabled = true;
        user.DiscordWebhookUrl = "https://discord.com/api/webhooks/user/token";
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 50m);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD");

        var outgoing = await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        var discordRequest = outgoing.OfType<SendAlertDiscordRequested>().FirstOrDefault();
        discordRequest.Should().NotBeNull();
        discordRequest!.DiscordWebhookUrl.Should().Be("https://discord.com/api/webhooks/user/token");
        discordRequest.ProductName.Should().Be("Widget");
        discordRequest.CurrentPrice.Should().Be(45m);
        discordRequest.TargetPrice.Should().Be(50m);
        discordRequest.Currency.Should().Be("USD");
    }

    [Theory]
    [InlineData(true, "42", true)]
    [InlineData(false, "42", false)]
    [InlineData(true, null, false)]
    public async Task HandleAsync_TelegramCascade_FollowsEnabledFlagAndChatId(bool enabled, string? chatId, bool expected)
    {
        var user = DbContext.Users.First(u => u.Id == TestUserId);
        user.TelegramNotificationsEnabled = enabled;
        user.TelegramChatId = chatId;
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outgoing = await FireAsync();

        var requests = outgoing.OfType<SendAlertTelegramRequested>().ToList();
        requests.Should().HaveCount(expected ? 1 : 0);
        if (expected)
        {
            requests[0].ChatId.Should().Be("42");
            requests[0].ProductName.Should().Be("Widget");
            requests[0].Condition.Should().Be(AlertCondition.Below);
        }
    }

    [Theory]
    [InlineData(true, "uQiRzpo4DXghDmr9QzzfQu27cmVRsG", true)]
    [InlineData(false, "uQiRzpo4DXghDmr9QzzfQu27cmVRsG", false)]
    [InlineData(true, null, false)]
    public async Task HandleAsync_PushoverCascade_FollowsEnabledFlagAndUserKey(bool enabled, string? userKey, bool expected)
    {
        var user = DbContext.Users.First(u => u.Id == TestUserId);
        user.PushoverNotificationsEnabled = enabled;
        user.PushoverUserKey = userKey;
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outgoing = await FireAsync();

        var requests = outgoing.OfType<SendAlertPushoverRequested>().ToList();
        requests.Should().HaveCount(expected ? 1 : 0);
        if (expected) requests[0].UserKey.Should().Be(userKey);
    }

    [Theory]
    [InlineData(true, "https://ntfy.sh/ophi-alerts", true)]
    [InlineData(false, "https://ntfy.sh/ophi-alerts", false)]
    [InlineData(true, null, false)]
    public async Task HandleAsync_NtfyCascade_FollowsEnabledFlagAndTopicUrl(bool enabled, string? topicUrl, bool expected)
    {
        var user = DbContext.Users.First(u => u.Id == TestUserId);
        user.NtfyNotificationsEnabled = enabled;
        user.NtfyTopicUrl = topicUrl;
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outgoing = await FireAsync();

        var requests = outgoing.OfType<SendAlertNtfyRequested>().ToList();
        requests.Should().HaveCount(expected ? 1 : 0);
        if (expected)
        {
            requests[0].TopicUrl.Should().Be(topicUrl);
            requests[0].Condition.Should().Be(AlertCondition.Below);
        }
    }

    private async Task<Wolverine.OutgoingMessages> FireAsync()
    {
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 50m);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return await SendAlertNotificationHandler.HandleAsync(
            new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD"),
            DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandleAsync_WhenUserHasDiscordDisabled_SkipsDiscordCascade()
    {
        var user = DbContext.Users.First(u => u.Id == TestUserId);
        user.DiscordNotificationsEnabled = false;
        user.DiscordWebhookUrl = "https://discord.com/api/webhooks/user/token";
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 50m);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD");

        var outgoing = await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        outgoing.OfType<SendAlertDiscordRequested>().Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenUserHasNoWebhookUrl_SkipsDiscordCascade()
    {
        var user = DbContext.Users.First(u => u.Id == TestUserId);
        user.DiscordNotificationsEnabled = true;
        user.DiscordWebhookUrl = null;
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 50m);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD");

        var outgoing = await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        outgoing.OfType<SendAlertDiscordRequested>().Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_UpdatesTriggerCountOnAlert()
    {
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.Alert(product.Id, TestUserId).WithTarget(50m).WithTriggerCount(5).Build();
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD");

        await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        var savedAlert = await DbContext.Alerts.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        savedAlert!.TriggerCount.Should().Be(6);
    }

    [Fact]
    public async Task HandleAsync_UpdatesLastTriggeredAtOnAlert()
    {
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var originalTime = DateTime.UtcNow.AddHours(-1);
        var alert = TestEntityFactory.Alert(product.Id, TestUserId).WithTarget(50m).LastTriggered(originalTime).Build();
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD");

        await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        var savedAlert = await DbContext.Alerts.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        savedAlert!.LastTriggeredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        savedAlert.LastTriggeredAt.Should().BeAfter(originalTime);
    }

    [Fact]
    public async Task HandleAsync_WithBelowCondition_MessageFormatting()
    {
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 50m, AlertCondition.Below);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45.50m, 50m, "GBP");

        await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        var notification = await DbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification!.Message.Should().Be("Price dropped to GBP 45.50 (target: GBP 50.00)");
    }

    [Fact]
    public async Task HandleAsync_WithAboveCondition_MessageFormatting()
    {
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 75m, AlertCondition.Above);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 80m, 75m, "EUR");

        await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        var notification = await DbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification!.Message.Should().Be("Price rose to EUR 80.00 (target: EUR 75.00)");
    }

    [Fact]
    public async Task HandleAsync_WithPercentDropCondition_MessageFormatting()
    {
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 20m, AlertCondition.PercentDrop);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 80m, 20m, "USD");

        await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        var notification = await DbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification!.Message.Should().Be("Price dropped by at least 20% to USD 80.00");
    }

    [Fact]
    public async Task HandleAsync_CascadesWebhookRequest()
    {
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        product.CurrentPrice = 45m;
        product.PreviousPrice = 60m;
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 50m);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD");

        var outgoing = await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        var webhookRequest = outgoing.OfType<SendAlertWebhookRequested>().FirstOrDefault();
        webhookRequest.Should().NotBeNull();
        webhookRequest!.AlertId.Should().Be(alert.Id);
        webhookRequest.UserId.Should().Be(TestUserId);
        webhookRequest.ProductId.Should().Be(product.Id);
        webhookRequest.ProductName.Should().Be("Widget");
        webhookRequest.OldPrice.Should().Be(60m);
        webhookRequest.CurrentPrice.Should().Be(45m);
        webhookRequest.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task HandleAsync_PersistsNotificationBeforeReturningCascades()
    {
        // Establishes the contract: the in-app notification is persisted in the same
        // DbContext save as the alert state. Cascaded events fire only after that commit,
        // so a downstream-handler failure can't unwind the notification.
        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 50m);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD");

        await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Re-query via fresh context to confirm SaveChanges actually committed.
        var notification = await DbContext.Notifications.AsNoTracking().FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_WhenUserHasEmailDisabled_SkipsEmailCascade()
    {
        var user = DbContext.Users.First(u => u.Id == TestUserId);
        user.EmailNotificationsEnabled = false;
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 50m);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD");

        var outgoing = await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        outgoing.OfType<SendAlertEmailRequested>().Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenUserHasEmailDisabled_StillNotifiesEveryOtherChannel()
    {
        // Opting out of email must not silently opt the user out of the alert itself. The in-app
        // notification, the webhook cascade and the live-update ping are independent channels.
        var user = DbContext.Users.First(u => u.Id == TestUserId);
        user.EmailNotificationsEnabled = false;
        user.DiscordNotificationsEnabled = true;
        user.DiscordWebhookUrl = "https://discord.com/api/webhooks/user/token";
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 50m);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD");

        var outgoing = await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        outgoing.OfType<SendAlertDiscordRequested>().Should().ContainSingle();
        outgoing.OfType<SendAlertWebhookRequested>().Should().ContainSingle();
        outgoing.OfType<LiveUpdate>().Should().ContainSingle();

        var notification = await DbContext.Notifications.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        notification.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_WhenUserHasEmailEnabled_CascadesEmailRequest()
    {
        // The default is on, so an untouched user keeps receiving alert email.
        var user = DbContext.Users.First(u => u.Id == TestUserId);
        user.EmailNotificationsEnabled.Should().BeTrue();

        var (product, productUrl) = TestEntityFactory.CreateProduct("Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 50m);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD");

        var outgoing = await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        outgoing.OfType<SendAlertEmailRequested>().Should().ContainSingle();
    }
}
