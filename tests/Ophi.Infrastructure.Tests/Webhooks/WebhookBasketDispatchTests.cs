using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Webhooks;
using Ophi.TestHelpers;

namespace Ophi.Infrastructure.Tests.Webhooks;

/// <summary>
/// <c>basket_alert_fired</c> (#151) has its own payload: a basket and a store, not a product. Only
/// targets that subscribed to it receive it.
/// </summary>
public class WebhookBasketDispatchTests
{
    [Fact]
    public async Task DispatchBasketAlertAsync_PostsTheBasketPayloadOnlyToSubscribedTargets()
    {
        var (db, connection) = TestDbContextFactory.Create();
        using var _ = connection;
        using var __ = db;
        var userId = Guid.NewGuid();
        db.Users.Add(TestEntityFactory.User(userId).Build());
        db.WebhookTargets.Add(new WebhookTarget { Id = Guid.NewGuid(), UserId = userId, Name = "basket", Url = "https://hooks.example.com/basket", Events = ["basket_alert_fired"] });
        db.WebhookTargets.Add(new WebhookTarget { Id = Guid.NewGuid(), UserId = userId, Name = "product", Url = "https://hooks.example.com/product", Events = ["alert_fired"] });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler);
        var service = new WebhookDispatchService(db, http, TimeProvider.System, NullLogger<WebhookDispatchService>.Instance);
        var basketId = Guid.NewGuid();

        await service.DispatchBasketAlertAsync(userId, new BasketAlertWebhookPayload(
            basketId, "Camping", "shop-a.es", 45m, 50m, "EUR", "https://ophi.example/baskets/x", DateTime.UtcNow),
            TestContext.Current.CancellationToken);

        handler.Requests.Should().ContainSingle().Which.Url.Should().Be("https://hooks.example.com/basket");
        using var json = JsonDocument.Parse(handler.Requests[0].Body);
        var root = json.RootElement;
        root.GetProperty("event").GetString().Should().Be("basket_alert_fired");
        root.GetProperty("basket_id").GetGuid().Should().Be(basketId);
        root.GetProperty("basket_name").GetString().Should().Be("Camping");
        root.GetProperty("store").GetString().Should().Be("shop-a.es");
        root.GetProperty("total").GetDecimal().Should().Be(45m);
        root.GetProperty("target_total").GetDecimal().Should().Be(50m);
        root.GetProperty("currency").GetString().Should().Be("EUR");
        root.GetProperty("url").GetString().Should().Be("https://ophi.example/baskets/x");
        root.TryGetProperty("product_id", out var __p).Should().BeFalse();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(string Url, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.ToString(), await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
