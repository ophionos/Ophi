using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Ophi.Domain.Entities;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Settings;
using Ophi.Infrastructure.Tests.Helpers;
using Ophi.Worker.Handlers;

namespace Ophi.Infrastructure.Tests.Handlers;

/// <summary>The basket-alert check (#151). The watched-store rules are BasketAlert's and BasketTotals'.</summary>
public class CheckBasketAlertsHandlerTests : HandlerTestBase
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
    private readonly IOptions<AlertSettings> _alertSettings = Options.Create(new AlertSettings { CooldownMinutes = 60, MaxAlertsPerUser = 100 });
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Now));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Product AddProduct(string name, params (string Host, decimal Price, string Currency)[] urls)
    {
        var product = new Product { Id = Guid.NewGuid(), UserId = TestUserId, Name = name, Currency = "EUR" };
        DbContext.Products.Add(product);
        foreach (var (host, price, currency) in urls)
        {
            DbContext.ProductUrls.Add(new ProductUrl
            {
                Id = Guid.NewGuid(), ProductId = product.Id, Url = $"https://www.{host}/{Guid.NewGuid():N}",
                CurrentPrice = price, Currency = currency
            });
        }
        return product;
    }

    private Basket AddBasket(params Product[] products)
    {
        var basket = new Basket { Id = Guid.NewGuid(), UserId = TestUserId, Name = "Desk" };
        foreach (var product in products) basket.AddItem(product.Id);
        DbContext.Baskets.Add(basket);
        return basket;
    }

    private BasketAlert AddAlert(Basket basket, decimal target, string? host = null, string currency = "EUR",
        bool active = true, DateTime? lastTriggeredAt = null)
    {
        var alert = new BasketAlert
        {
            Id = Guid.NewGuid(), BasketId = basket.Id, UserId = TestUserId, TargetTotal = target, Currency = currency,
            Host = host, IsActive = active, LastTriggeredAt = lastTriggeredAt
        };
        DbContext.BasketAlerts.Add(alert);
        return alert;
    }

    private async Task<List<BasketAlertTriggeredEvent>> Check(Product changed)
    {
        await DbContext.SaveChangesAsync(Ct);
        DbContext.ChangeTracker.Clear();
        var result = await CheckBasketAlertsHandler.HandleAsync(
            new PriceUpdatedEvent(changed.Id, 20m, 10m, "EUR"), DbContext, _alertSettings, _time, LoggerMock.Object, Ct);
        return result.ToList();
    }

    [Fact]
    public async Task HandleAsync_BestCompleteStoreUnderTarget_TriggersAndClaims()
    {
        var mouse = AddProduct("Mouse", ("one.example", 10m, "EUR"), ("two.example", 8m, "EUR"));
        var pad = AddProduct("Pad", ("one.example", 20m, "EUR"));
        DbContext.ShippingRates.Add(new ShippingRate { Id = Guid.NewGuid(), UserId = TestUserId, Host = "one.example", Amount = 4m, Currency = "EUR" });
        var basket = AddBasket(mouse, pad);
        var alert = AddAlert(basket, 34m);

        var events = await Check(mouse);

        // two.example is cheaper for the mouse but lacks the pad, so one.example (30 + 4) is the best store.
        events.Should().Equal(new BasketAlertTriggeredEvent(alert.Id, basket.Id, TestUserId, "one.example", 34m, 34m, "EUR"));
        (await DbContext.BasketAlerts.SingleAsync(a => a.Id == alert.Id, Ct)).LastTriggeredAt.Should().Be(Now);
    }

    [Fact]
    public async Task HandleAsync_AlertOnAnIncompleteStore_DoesNotTrigger()
    {
        var mouse = AddProduct("Mouse", ("one.example", 10m, "EUR"), ("two.example", 8m, "EUR"));
        var pad = AddProduct("Pad", ("one.example", 20m, "EUR"));
        var basket = AddBasket(mouse, pad);
        AddAlert(basket, 100m, host: "two.example");

        (await Check(mouse)).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_HostAlert_IgnoresACheaperStore()
    {
        var mouse = AddProduct("Mouse", ("one.example", 10m, "EUR"), ("two.example", 50m, "EUR"));
        var basket = AddBasket(mouse);
        AddAlert(basket, 20m, host: "two.example");

        (await Check(mouse)).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_InCooldown_DoesNotTrigger()
    {
        var mouse = AddProduct("Mouse", ("one.example", 10m, "EUR"));
        var basket = AddBasket(mouse);
        AddAlert(basket, 20m, lastTriggeredAt: Now.AddMinutes(-30));

        (await Check(mouse)).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_TotalInAnotherCurrency_IsDormant()
    {
        var mouse = AddProduct("Mouse", ("one.example", 10m, "USD"));
        var basket = AddBasket(mouse);
        AddAlert(basket, 20m, currency: "EUR");

        (await Check(mouse)).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_PausedAlertOrBasketWithoutTheProduct_IsNotChecked()
    {
        var mouse = AddProduct("Mouse", ("one.example", 10m, "EUR"));
        var pad = AddProduct("Pad", ("one.example", 5m, "EUR"));
        AddAlert(AddBasket(mouse), 20m, active: false);
        AddAlert(AddBasket(pad), 20m);

        (await Check(mouse)).Should().BeEmpty();
    }
}
