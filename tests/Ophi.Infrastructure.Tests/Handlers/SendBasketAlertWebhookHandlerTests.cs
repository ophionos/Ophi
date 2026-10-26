using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Webhooks;
using Ophi.Worker.Handlers;

namespace Ophi.Infrastructure.Tests.Handlers;

public class SendBasketAlertWebhookHandlerTests
{
    [Fact]
    public async Task HandleAsync_DispatchesTheBasketAlertForTheUser()
    {
        var dispatch = new Mock<IWebhookDispatchService>();
        var userId = Guid.NewGuid();
        var basketId = Guid.NewGuid();

        await SendBasketAlertWebhookHandler.HandleAsync(
            new SendBasketAlertWebhookRequested(Guid.NewGuid(), userId, basketId, "Camping", "shop-a.es", 45m, 50m, "EUR", "https://ophi.example/baskets/x"),
            dispatch.Object, TimeProvider.System, NullLogger.Instance, TestContext.Current.CancellationToken);

        dispatch.Verify(d => d.DispatchBasketAlertAsync(
            userId,
            It.Is<BasketAlertWebhookPayload>(p =>
                p.BasketId == basketId && p.BasketName == "Camping" && p.Store == "shop-a.es" &&
                p.Total == 45m && p.TargetTotal == 50m && p.Currency == "EUR" && p.Url == "https://ophi.example/baskets/x"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
