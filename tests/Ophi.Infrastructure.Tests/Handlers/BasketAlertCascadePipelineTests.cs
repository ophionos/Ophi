using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Ophi.Domain.Entities;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Discord;
using Ophi.Infrastructure.Email;
using Ophi.Infrastructure.Settings;
using Ophi.Infrastructure.Tests.Helpers;
using Ophi.Infrastructure.Webhooks;
using Ophi.Worker.Handlers;

namespace Ophi.Infrastructure.Tests.Handlers;

/// <summary>
/// The basket form of <see cref="AlertCascadePipelineTests"/> (#151): a price change runs
/// <see cref="CheckBasketAlertsHandler"/>, its event runs <see cref="SendBasketAlertNotificationHandler"/>,
/// and the cascaded messages reach the shared email and Discord handlers in the basket form and
/// <see cref="SendBasketAlertWebhookHandler"/> as <c>basket_alert_fired</c>. The cascade is fanned out
/// by hand, as Wolverine does in production.
/// </summary>
public class BasketAlertCascadePipelineTests : HandlerTestBase
{
    private readonly Mock<IEmailService> _emailServiceMock = new();
    private readonly Mock<IDiscordService> _discordServiceMock = new();
    private readonly Mock<IWebhookDispatchService> _webhookDispatchServiceMock = new();
    private readonly IOptions<AlertSettings> _alertSettings = Options.Create(new AlertSettings { CooldownMinutes = 60, MaxAlertsPerUser = 100 });
    private readonly IOptions<EmailSettings> _emailSettings = Options.Create(new EmailSettings { AppUrl = "https://ophi.example" });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FullCascade_DeliversTheBasketFormToEveryChannel()
    {
        TestUser.EmailNotificationsEnabled = true;
        TestUser.DiscordNotificationsEnabled = true;
        TestUser.DiscordWebhookUrl = "https://discord.com/api/webhooks/abc/def";

        var product = new Product { Id = Guid.NewGuid(), UserId = TestUserId, Name = "Tent", Currency = "EUR" };
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://www.shop-a.example/tent", CurrentPrice = 45m, Currency = "EUR"
        });
        var basket = new Basket { Id = Guid.NewGuid(), UserId = TestUserId, Name = "Camping" };
        basket.AddItem(product.Id);
        DbContext.Baskets.Add(basket);
        DbContext.BasketAlerts.Add(new BasketAlert
        {
            Id = Guid.NewGuid(), BasketId = basket.Id, UserId = TestUserId, TargetTotal = 50m, Currency = "EUR", IsActive = true
        });
        await DbContext.SaveChangesAsync(Ct);
        var link = $"https://ophi.example/baskets/{basket.Id}";

        var triggered = (await CheckBasketAlertsHandler.HandleAsync(
            new PriceUpdatedEvent(product.Id, 60m, 45m, "EUR"), DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, Ct)).ToList();

        triggered.Should().ContainSingle();

        foreach (var ev in triggered)
        {
            var outgoing = await SendBasketAlertNotificationHandler.HandleAsync(
                ev, DbContext, _emailSettings, TimeProvider.System, LoggerMock.Object, Ct);

            foreach (var emailReq in outgoing.OfType<SendAlertEmailRequested>())
                await SendAlertEmailHandler.HandleAsync(emailReq, _emailServiceMock.Object, NullLogger.Instance, Ct);
            foreach (var discordReq in outgoing.OfType<SendAlertDiscordRequested>())
                await SendAlertDiscordHandler.HandleAsync(discordReq, _discordServiceMock.Object, NullLogger.Instance, Ct);
            foreach (var webhookReq in outgoing.OfType<SendBasketAlertWebhookRequested>())
                await SendBasketAlertWebhookHandler.HandleAsync(
                    webhookReq, _webhookDispatchServiceMock.Object, TimeProvider.System, NullLogger.Instance, Ct);

            outgoing.OfType<SendAlertWebhookRequested>().Should().BeEmpty("a basket fire is not a product alert_fired webhook");
        }

        _emailServiceMock.Verify(e => e.SendPriceAlertAsync(
            It.Is<PriceAlertEmail>(p => p.ProductName == "Camping" && p.Store == "shop-a.example" && p.CurrentPrice == 45m && p.ProductUrl == link),
            It.IsAny<CancellationToken>()), Times.Once);
        _discordServiceMock.Verify(d => d.SendPriceAlertAsync(
            It.Is<DiscordPriceAlert>(a => a.ProductName == "Camping" && a.Store == "shop-a.example"),
            "https://discord.com/api/webhooks/abc/def",
            It.IsAny<CancellationToken>()), Times.Once);
        _webhookDispatchServiceMock.Verify(w => w.DispatchBasketAlertAsync(
            TestUserId,
            It.Is<BasketAlertWebhookPayload>(p => p.BasketId == basket.Id && p.Store == "shop-a.example" && p.Total == 45m && p.TargetTotal == 50m),
            It.IsAny<CancellationToken>()), Times.Once);
        _webhookDispatchServiceMock.Verify(w => w.DispatchAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<WebhookPayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
