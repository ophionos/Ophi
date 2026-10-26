using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Postgres.Tests;

/// <summary>The (UserId, Host) unique index on real Postgres: one rate per host per user.</summary>
[Collection("Postgres")]
public class ShippingRateIndexTests(PostgresFixture fixture)
{
    private static ShippingRate Rate(Guid userId, string host) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, Host = host, Amount = 4.99m, Currency = "EUR", FreeAbove = 29.5m
    };

    [Fact]
    public async Task SaveChanges_WithDuplicateUserAndHost_ThrowsUniqueViolation()
    {
        var userId = Guid.NewGuid();
        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(userId).Build());
            ctx.ShippingRates.Add(Rate(userId, "amazon.es"));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var dup = fixture.CreateContext();
        dup.ShippingRates.Add(Rate(userId, "amazon.es"));
        var act = () => dup.SaveChangesAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SaveChanges_WithSameHostForTwoUsers_Succeeds_AndAmountsRoundTrip()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(a).Build());
            ctx.Users.Add(TestEntityFactory.User(b).Build());
            ctx.ShippingRates.Add(Rate(a, "amazon.es"));
            ctx.ShippingRates.Add(Rate(b, "amazon.es"));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = fixture.CreateContext();
        var rows = await verify.ShippingRates.Where(r => r.UserId == a || r.UserId == b).ToListAsync(TestContext.Current.CancellationToken);
        rows.Should().HaveCount(2).And.OnlyContain(r => r.Amount == 4.99m && r.FreeAbove == 29.5m);
    }
}
