using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Settings;
using Ophi.TestHelpers;
using Ophi.Worker.Handlers;

namespace Ophi.Postgres.Tests;

/// <summary>
/// Issue #18 on real Postgres: the Alert xmin optimistic-concurrency token must turn a cross-worker
/// alert-firing race into a <see cref="DbUpdateConcurrencyException"/> the worker can absorb, so two
/// worker processes can't double-fire. xmin only exists on Postgres, so the SQLite unit tier cannot
/// cover this — and a naive "two concurrent events → one notification" test would pass via the cooldown
/// check without the token ever engaging. These two tests are deterministic and exercise the token
/// directly.
/// </summary>
[Collection("Postgres")]
public class AlertConcurrencyTests(PostgresFixture fixture)
{
    private static readonly IOptions<AlertSettings> AlertSettings =
        Options.Create(new AlertSettings { CooldownMinutes = 60, MaxAlertsPerUser = 100 });

    [Fact]
    public async Task Alert_HasXminConcurrencyToken_SecondWriteThrows()
    {
        // Proves xmin is actually wired as a concurrency token: if it weren't, both saves would
        // succeed and no exception would be thrown.
        var alertId = await SeedTriggerableAlertAsync();

        await using var ctx1 = fixture.CreateContext();
        await using var ctx2 = fixture.CreateContext();

        var a1 = await ctx1.Alerts.SingleAsync(a => a.Id == alertId, TestContext.Current.CancellationToken);
        var a2 = await ctx2.Alerts.SingleAsync(a => a.Id == alertId, TestContext.Current.CancellationToken);

        a1.TriggerCount += 1;
        await ctx1.SaveChangesAsync(TestContext.Current.CancellationToken); // wins; bumps xmin

        a2.TriggerCount += 1;
        var act = async () => await ctx2.SaveChangesAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "the second writer holds a stale xmin and must lose the race");
    }

    [Fact]
    public async Task CheckAlertsHandler_OnConcurrencyConflict_EmitsNothing_AndDoesNotFire()
    {
        // Deterministically forces the conflict without racing: the handler's context pre-tracks the
        // alert (stale xmin), then a second context bumps the row. When the handler stamps
        // LastTriggeredAt and saves, its WHERE xmin = <stale> matches 0 rows → DbUpdateConcurrencyException
        // → the handler's catch → returns []. This exercises the real lost-write path end to end.
        var alertId = await SeedTriggerableAlertAsync();
        var productId = await ProductIdForAsync(alertId);

        await using var handlerCtx = fixture.CreateContext();

        // Pre-load (and thus track, with the current xmin) the same way the handler queries.
        _ = await handlerCtx.Alerts
            .Where(a => a.IsActive && a.ProductId == productId)
            .Include(a => a.Product)
            .Include(a => a.User)
            .ToListAsync(TestContext.Current.CancellationToken);

        // A concurrent worker bumps the alert row, advancing its xmin past what handlerCtx tracks.
        await using (var otherCtx = fixture.CreateContext())
        {
            var other = await otherCtx.Alerts.SingleAsync(a => a.Id == alertId, TestContext.Current.CancellationToken);
            other.TriggerCount += 1;
            await otherCtx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act — handler runs against its stale-tracked context.
        var result = await CheckAlertsHandler.HandleAsync(
            new PriceUpdatedEvent(productId, null, 45m, "USD", Guid.NewGuid()),
            handlerCtx, AlertSettings, TimeProvider.System, NullLogger.Instance,
            TestContext.Current.CancellationToken);

        // Assert — no events emitted, and the lost write means LastTriggeredAt never committed.
        result.Should().BeEmpty("the conflicting batch must be dropped, not re-fired");

        await using var verifyCtx = fixture.CreateContext();
        var alert = await verifyCtx.Alerts.SingleAsync(a => a.Id == alertId, TestContext.Current.CancellationToken);
        alert.LastTriggeredAt.Should().BeNull("the losing handler's stamp must have rolled back");
    }

    private async Task<Guid> SeedTriggerableAlertAsync()
    {
        var userId = Guid.NewGuid();
        var alertId = Guid.NewGuid();

        await using var ctx = fixture.CreateContext();
        ctx.Users.Add(TestEntityFactory.User(userId).Build());

        // CurrentPrice 45 is below the alert target 50, so a Below alert should trigger.
        var product = TestEntityFactory.Product(userId).Named("Widget").Priced(45m).Build();
        ctx.Products.Add(product);
        ctx.ProductUrls.Add(TestEntityFactory.ProductUrl(product.Id).Priced(45m).Build());
        ctx.Alerts.Add(new Alert
        {
            Id = alertId,
            ProductId = product.Id,
            UserId = userId,
            TargetPrice = 50m,
            ReferencePrice = 100m,
            Condition = AlertCondition.Below,
            IsActive = true
        });

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return alertId;
    }

    private async Task<Guid> ProductIdForAsync(Guid alertId)
    {
        await using var ctx = fixture.CreateContext();
        return await ctx.Alerts.Where(a => a.Id == alertId).Select(a => a.ProductId)
            .SingleAsync(TestContext.Current.CancellationToken);
    }
}
