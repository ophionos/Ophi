using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Ophi.Domain.Messages.Commands;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Tests.Helpers;
using Ophi.Infrastructure.Webhooks;
using Ophi.TestHelpers;
using Ophi.Worker.Handlers;
using Ophi.Worker.Settings;
using Wolverine;

namespace Ophi.Infrastructure.Tests.Handlers;

/// <summary>
/// Account deletion (UX-5) hard-deletes the user and cascades every owned row, but messages
/// referencing those rows can already be in flight on the scraping/events queues. These tests
/// pin the tolerance contract: a handler that meets a vanished row must return quietly, not
/// throw — a throw would park the message as poison and retry against a row that will never
/// come back (the failure mode the Notification.Title overflow incident made expensive).
/// </summary>
public class DeletedUserMessageToleranceTests : HandlerTestBase
{
    [Fact]
    public async Task CheckProductPrice_AfterUserDeleted_ReturnsNullWithoutScraping()
    {
        var (product, productUrl) = TestEntityFactory.CreateProduct("Doomed Widget", TestUserId);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // The account-deletion path: remove the user; product + URL cascade away.
        DbContext.Users.Remove(TestUser);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var scrapingServiceMock = new Mock<IScrapingService>();
        var workerSettingsMock = new Mock<IOptions<WorkerSettings>>();
        workerSettingsMock.Setup(x => x.Value).Returns(new WorkerSettings());
        var command = new CheckProductUrlPriceCommand(productUrl.Id);

        var result = await CheckProductPriceHandler.HandleAsync(
            command, DbContext, scrapingServiceMock.Object, workerSettingsMock.Object,
            Mock.Of<IWebhookDispatchService>(), TimeProvider.System, Mock.Of<IMessageBus>(),
            LoggerMock.Object, TestContext.Current.CancellationToken);

        result.Should().BeNull();
        scrapingServiceMock.Verify(
            x => x.ScrapeProductAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendAlertNotification_AfterUserDeleted_ReturnsEmptyAndPersistsNothing()
    {
        var (product, productUrl) = TestEntityFactory.CreateProduct("Doomed Widget", TestUserId);
        var alert = TestEntityFactory.CreateAlert(product.Id, TestUserId, 50m);
        DbContext.Products.Add(product);
        DbContext.ProductUrls.Add(productUrl);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // The alert fired, then the user deleted their account before the event was handled.
        var @event = new AlertTriggeredEvent(alert.Id, product.Id, TestUserId, 45m, 50m, "USD");
        DbContext.Users.Remove(TestUser);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outgoing = await SendAlertNotificationHandler.HandleAsync(
            @event, DbContext, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        outgoing.Should().BeEmpty();
        (await DbContext.Notifications.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }
}
