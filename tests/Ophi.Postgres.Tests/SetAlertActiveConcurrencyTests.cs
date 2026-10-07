using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ophi.Api.Features.Alerts;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Settings;
using Ophi.TestHelpers;

namespace Ophi.Postgres.Tests;

/// <summary>
/// <c>SetAlertActive</c> is a second writer on the xmin-tokened <see cref="Alert"/> row, racing
/// <c>CheckAlertsHandler</c>'s fire claim. SQLite has no xmin, so only this tier can prove the
/// handler's reload-and-reapply retry. Same stale-context technique as
/// <see cref="AlertConcurrencyTests"/>: deterministic, no timing races.
/// </summary>
[Collection("Postgres")]
public class SetAlertActiveConcurrencyTests(PostgresFixture fixture)
{
    private static readonly IOptions<AlertSettings> AlertSettings =
        Options.Create(new AlertSettings { CooldownMinutes = 60, MaxAlertsPerUser = 100 });

    [Fact]
    public async Task Pause_WhenAnotherWriterBumpedTheRow_RetriesAndKeepsBothWrites()
    {
        var (alertId, userId) = await SeedActiveAlertAsync();

        await using var handlerCtx = fixture.CreateContext();

        // Track the alert with its current xmin; the handler's own query then resolves to this
        // stale instance (EF identity resolution does not refresh tracked values).
        _ = await handlerCtx.Alerts.SingleAsync(a => a.Id == alertId, TestContext.Current.CancellationToken);

        // Stand-in for the checker's fire claim landing first.
        var firedAt = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        await using (var otherCtx = fixture.CreateContext())
        {
            var other = await otherCtx.Alerts.SingleAsync(a => a.Id == alertId, TestContext.Current.CancellationToken);
            other.LastTriggeredAt = firedAt;
            await otherCtx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var handler = new SetAlertActive.Handler(
            handlerCtx, AlertSettings, NullLogger<SetAlertActive.Handler>.Instance);

        var result = await handler.Handle(
            new SetAlertActive.Command(alertId, false) { UserId = userId },
            TestContext.Current.CancellationToken);

        result.Active.Should().BeFalse();

        await using var verifyCtx = fixture.CreateContext();
        var alert = await verifyCtx.Alerts.SingleAsync(a => a.Id == alertId, TestContext.Current.CancellationToken);
        alert.IsActive.Should().BeFalse("the retry must reapply the pause after reloading");
        alert.LastTriggeredAt.Should().Be(firedAt, "the reload must not clobber the other writer's claim");
    }

    private async Task<(Guid alertId, Guid userId)> SeedActiveAlertAsync()
    {
        var userId = Guid.NewGuid();
        var alertId = Guid.NewGuid();

        await using var ctx = fixture.CreateContext();
        ctx.Users.Add(TestEntityFactory.User(userId).Build());
        var product = TestEntityFactory.Product(userId).Named("Widget").Priced(45m).Build();
        ctx.Products.Add(product);
        ctx.Alerts.Add(new Alert
        {
            Id = alertId,
            ProductId = product.Id,
            UserId = userId,
            TargetPrice = 50m,
            Condition = AlertCondition.Below,
            IsActive = true
        });

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (alertId, userId);
    }
}
