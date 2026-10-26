using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ophi.Api.Features.Baskets;
using Ophi.Domain.Entities;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Settings;
using Ophi.TestHelpers;
using Ophi.Worker.Handlers;

namespace Ophi.Postgres.Tests;

/// <summary>
/// The BasketAlert xmin token on real Postgres (#151), as <see cref="AlertConcurrencyTests"/> does for
/// product alerts. The handler's context tracks the alert before a second context changes the row, so
/// the conflict is forced without a race.
/// </summary>
[Collection("Postgres")]
public class BasketAlertConcurrencyTests(PostgresFixture fixture)
{
    private static readonly IOptions<AlertSettings> AlertSettings =
        Options.Create(new AlertSettings { CooldownMinutes = 60, MaxAlertsPerUser = 100 });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BasketAlert_HasXminConcurrencyToken_SecondWriteThrows()
    {
        var (alertId, _, _) = await SeedTriggerableBasketAlertAsync();

        await using var ctx1 = fixture.CreateContext();
        await using var ctx2 = fixture.CreateContext();
        var a1 = await ctx1.BasketAlerts.SingleAsync(a => a.Id == alertId, Ct);
        var a2 = await ctx2.BasketAlerts.SingleAsync(a => a.Id == alertId, Ct);

        a1.ClaimFiring(DateTime.UtcNow);
        await ctx1.SaveChangesAsync(Ct);

        a2.ClaimFiring(DateTime.UtcNow);
        var act = async () => await ctx2.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>("the second writer holds a stale xmin");
    }

    [Fact]
    public async Task CheckBasketAlertsHandler_OnConcurrencyConflict_EmitsNothing_AndDoesNotStamp()
    {
        var (alertId, _, productId) = await SeedTriggerableBasketAlertAsync();

        await using var handlerCtx = fixture.CreateContext();
        _ = await handlerCtx.BasketAlerts.SingleAsync(a => a.Id == alertId, Ct);

        // Another worker changes the row; only TriggerCount moves, so the handler's query still matches.
        await using (var otherCtx = fixture.CreateContext())
        {
            await otherCtx.BasketAlerts.Where(a => a.Id == alertId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.TriggerCount, a => a.TriggerCount + 1), Ct);
        }

        var result = await CheckBasketAlertsHandler.HandleAsync(
            new PriceUpdatedEvent(productId, 60m, 45m, "EUR"), handlerCtx, AlertSettings, TimeProvider.System, NullLogger.Instance, Ct);

        result.Should().BeEmpty("the conflicting batch must be dropped, not fired");
        await using var verifyCtx = fixture.CreateContext();
        (await verifyCtx.BasketAlerts.SingleAsync(a => a.Id == alertId, Ct)).LastTriggeredAt
            .Should().BeNull("the losing claim must have rolled back");
    }

    [Fact]
    public async Task SetBasketAlertActive_AfterAConcurrentClaim_ReappliesThePauseAndKeepsTheClaim()
    {
        var (alertId, basketId, _) = await SeedTriggerableBasketAlertAsync();
        var userId = await UserIdForAsync(alertId);
        var claimedAt = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

        await using var handlerCtx = fixture.CreateContext();
        _ = await handlerCtx.BasketAlerts.SingleAsync(a => a.Id == alertId, Ct);

        // A worker claims the firing after the handler's read.
        await using (var workerCtx = fixture.CreateContext())
        {
            var claimed = await workerCtx.BasketAlerts.SingleAsync(a => a.Id == alertId, Ct);
            claimed.ClaimFiring(claimedAt);
            await workerCtx.SaveChangesAsync(Ct);
        }

        var handler = new SetBasketAlertActive.Handler(handlerCtx, AlertSettings, NullLogger<SetBasketAlertActive.Handler>.Instance);
        var dto = await handler.Handle(new SetBasketAlertActive.Command(basketId, alertId, false) { UserId = userId }, Ct);

        dto.Active.Should().BeFalse();
        await using var verifyCtx = fixture.CreateContext();
        var alert = await verifyCtx.BasketAlerts.SingleAsync(a => a.Id == alertId, Ct);
        alert.IsActive.Should().BeFalse("the pause is reapplied after the reload");
        alert.LastTriggeredAt.Should().Be(claimedAt, "the reload keeps the worker's claim");
    }

    private async Task<(Guid AlertId, Guid BasketId, Guid ProductId)> SeedTriggerableBasketAlertAsync()
    {
        var userId = Guid.NewGuid();
        await using var ctx = fixture.CreateContext();
        ctx.Users.Add(TestEntityFactory.User(userId).Build());

        // One store with a total of 45, under the target of 50.
        var product = TestEntityFactory.Product(userId).Named("Tent").Priced(45m).Build();
        ctx.Products.Add(product);
        ctx.ProductUrls.Add(TestEntityFactory.ProductUrl(product.Id).Priced(45m).Build());
        var basket = new Basket { Id = Guid.NewGuid(), UserId = userId, Name = "Camping" };
        basket.AddItem(product.Id);
        ctx.Baskets.Add(basket);
        var alert = new BasketAlert
        {
            Id = Guid.NewGuid(), BasketId = basket.Id, UserId = userId, TargetTotal = 50m, Currency = product.Currency, IsActive = true
        };
        ctx.BasketAlerts.Add(alert);

        await ctx.SaveChangesAsync(Ct);
        return (alert.Id, basket.Id, product.Id);
    }

    private async Task<Guid> UserIdForAsync(Guid alertId)
    {
        await using var ctx = fixture.CreateContext();
        return await ctx.BasketAlerts.Where(a => a.Id == alertId).Select(a => a.UserId).SingleAsync(Ct);
    }
}
