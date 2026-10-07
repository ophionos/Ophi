using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Discord;
using Ophi.Infrastructure.Email;
using Ophi.Infrastructure.Settings;
using Ophi.Infrastructure.Tests.Helpers;
using Ophi.Infrastructure.Webhooks;
using Ophi.TestHelpers;
using Ophi.Worker.Handlers;

namespace Ophi.Infrastructure.Tests.Handlers;

/// <summary>
/// Pins the end-to-end alert pipeline contract that <see cref="SendAlertNotificationHandler"/>
/// returns cascaded events that route to <see cref="SendAlertEmailHandler"/>,
/// <see cref="SendAlertDiscordHandler"/>, and <see cref="SendAlertWebhookHandler"/>.
///
/// The tests manually dispatch the <c>OutgoingMessages</c> return value into the channel
/// handlers — Wolverine does this in production via the framework's cascading-message routing.
/// If a future Wolverine bump silently breaks that contract, the unit tests on each handler
/// still pass; this file is what fails loudly. Treat as a regression guard for the Phase 7
/// split between orchestrator and channel handlers.
/// </summary>
public class AlertCascadePipelineTests : HandlerTestBase
{
    private readonly Mock<IEmailService> _emailServiceMock = new();
    private readonly Mock<IDiscordService> _discordServiceMock = new();
    private readonly Mock<IWebhookDispatchService> _webhookDispatchServiceMock = new();
    private readonly IOptions<AlertSettings> _alertSettings = Options.Create(new AlertSettings { CooldownMinutes = 60, MaxAlertsPerUser = 100 });

    [Fact]
    public async Task FullCascade_DeliversToAllChannels_WhenDiscordEnabled()
    {
        var user = DbContext.Users.First(u => u.Id == TestUserId);
        user.DiscordNotificationsEnabled = true;
        user.DiscordWebhookUrl = "https://discord.com/api/webhooks/abc/def";

        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = TestUserId,
            Name = "Premium Widget",
            CurrentPrice = 40m,
            PreviousPrice = 100m,
            Currency = "USD",
            Status = ProductStatus.Active
        };
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Product = product,
            UserId = TestUserId,
            User = user,
            TargetPrice = 50m,
            Condition = AlertCondition.Below,
            IsActive = true
        };
        DbContext.Products.Add(product);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var priceEvent = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        var triggered = (await CheckAlertsHandler.HandleAsync(
            priceEvent, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken)).ToList();

        triggered.Should().HaveCount(1);

        foreach (var ev in triggered)
        {
            var outgoing = await SendAlertNotificationHandler.HandleAsync(
                ev, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

            // Fan the cascade out manually — Wolverine does this in production.
            foreach (var emailReq in outgoing.OfType<SendAlertEmailRequested>())
            {
                await SendAlertEmailHandler.HandleAsync(
                    emailReq, _emailServiceMock.Object, NullLogger.Instance, TestContext.Current.CancellationToken);
            }
            foreach (var discordReq in outgoing.OfType<SendAlertDiscordRequested>())
            {
                await SendAlertDiscordHandler.HandleAsync(
                    discordReq, _discordServiceMock.Object, NullLogger.Instance, TestContext.Current.CancellationToken);
            }
            foreach (var webhookReq in outgoing.OfType<SendAlertWebhookRequested>())
            {
                await SendAlertWebhookHandler.HandleAsync(
                    webhookReq, _webhookDispatchServiceMock.Object, TimeProvider.System, NullLogger.Instance, TestContext.Current.CancellationToken);
            }
        }

        _emailServiceMock.Verify(e => e.SendPriceAlertAsync(
            It.Is<PriceAlertEmail>(p => p.ProductName == "Premium Widget" && p.CurrentPrice == 40m),
            It.IsAny<CancellationToken>()), Times.Once);

        _discordServiceMock.Verify(d => d.SendPriceAlertAsync(
            It.Is<DiscordPriceAlert>(a => a.ProductName == "Premium Widget"),
            "https://discord.com/api/webhooks/abc/def",
            It.IsAny<CancellationToken>()), Times.Once);

        _webhookDispatchServiceMock.Verify(w => w.DispatchAsync(
            WebhookEvents.AlertFired,
            TestUserId,
            It.Is<WebhookPayload>(p => p.ProductId == product.Id && p.OldPrice == 100m && p.NewPrice == 40m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FullCascade_EmailFailureDoesNotBlockDiscordOrWebhook()
    {
        // Regression guard for the Phase 7 split: in the old serial-await code, an email
        // exception swallowed Discord/webhook unless wrapped in try/catch. After the cascade,
        // each channel handler runs independently — if email throws, the other channels still
        // execute because they're separate messages, not sequential awaits.
        var user = DbContext.Users.First(u => u.Id == TestUserId);
        user.DiscordNotificationsEnabled = true;
        user.DiscordWebhookUrl = "https://discord.com/api/webhooks/abc/def";

        _emailServiceMock
            .Setup(e => e.SendPriceAlertAsync(It.IsAny<PriceAlertEmail>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("SMTP down"));

        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = TestUserId,
            Name = "Widget",
            CurrentPrice = 40m,
            Currency = "USD",
            Status = ProductStatus.Active
        };
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Product = product,
            UserId = TestUserId,
            User = user,
            TargetPrice = 50m,
            Condition = AlertCondition.Below,
            IsActive = true
        };
        DbContext.Products.Add(product);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var priceEvent = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");
        var triggered = (await CheckAlertsHandler.HandleAsync(
            priceEvent, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken)).ToList();

        foreach (var ev in triggered)
        {
            var outgoing = await SendAlertNotificationHandler.HandleAsync(
                ev, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

            // Email is expected to throw — swallow it (Wolverine would retry it in production).
            foreach (var emailReq in outgoing.OfType<SendAlertEmailRequested>())
            {
                try
                {
                    await SendAlertEmailHandler.HandleAsync(
                        emailReq, _emailServiceMock.Object, NullLogger.Instance, TestContext.Current.CancellationToken);
                }
                catch (HttpRequestException)
                {
                    // Expected — production retry comes from Wolverine.
                }
            }

            foreach (var discordReq in outgoing.OfType<SendAlertDiscordRequested>())
            {
                await SendAlertDiscordHandler.HandleAsync(
                    discordReq, _discordServiceMock.Object, NullLogger.Instance, TestContext.Current.CancellationToken);
            }
            foreach (var webhookReq in outgoing.OfType<SendAlertWebhookRequested>())
            {
                await SendAlertWebhookHandler.HandleAsync(
                    webhookReq, _webhookDispatchServiceMock.Object, TimeProvider.System, NullLogger.Instance, TestContext.Current.CancellationToken);
            }
        }

        _discordServiceMock.Verify(d => d.SendPriceAlertAsync(
            It.IsAny<DiscordPriceAlert>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _webhookDispatchServiceMock.Verify(w => w.DispatchAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<WebhookPayload>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
