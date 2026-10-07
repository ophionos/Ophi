using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Migrations;
using Ophi.TestHelpers;

namespace Ophi.Postgres.Tests;

/// <summary>
/// Covers the data half of the AddAlertCurrency migration on real Postgres.
///
/// The fixture applies the migration history to an empty database, which proves the DDL parses but
/// says nothing about the backfill — there are no rows to backfill. The backfill is the part that
/// can actually hurt: the scaffolded column default is <c>""</c>, and an alert denominated in <c>""</c>
/// matches no product currency, so skipping the backfill would silently mute every alert that
/// existed before the deploy. These tests execute the exact statement the migration ships
/// (<see cref="AddAlertCurrency.BackfillSql"/>) against seeded rows.
///
/// Postgres rather than SQLite because the correlated-subquery UPDATE and the GUID comparison in its
/// WHERE clause are provider-sensitive — SQLite's loose type affinity would pass a join that real
/// Postgres rejects on a uuid/text mismatch.
/// </summary>
[Collection("Postgres")]
public class AlertCurrencyBackfillTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Backfill_AdoptsEachAlertsOwnProductCurrency()
    {
        await using var ctx = fixture.CreateContext();

        var user = TestEntityFactory.User().Build();
        ctx.Users.Add(user);

        // Two products in different currencies, so a backfill that picked one global value (or
        // joined on the wrong row) would show up rather than passing by luck.
        var euroProduct = NewProduct(user.Id, "EUR");
        var poundProduct = NewProduct(user.Id, "GBP");
        ctx.Products.AddRange(euroProduct, poundProduct);

        var euroAlert = NewAlert(euroProduct, user.Id);
        var poundAlert = NewAlert(poundProduct, user.Id);
        ctx.Alerts.AddRange(euroAlert, poundAlert);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Reset to the pre-migration state the scaffolded default would have left behind.
        await ctx.Database.ExecuteSqlRawAsync(
            """UPDATE "Alerts" SET "Currency" = '' WHERE "Id" = {0} OR "Id" = {1}""",
            [euroAlert.Id, poundAlert.Id],
            TestContext.Current.CancellationToken);

        await ctx.Database.ExecuteSqlRawAsync(
            AddAlertCurrency.BackfillSql, TestContext.Current.CancellationToken);

        var reloaded = await ctx.Alerts.AsNoTracking()
            .Where(a => a.Id == euroAlert.Id || a.Id == poundAlert.Id)
            .ToDictionaryAsync(a => a.Id, a => a.Currency, TestContext.Current.CancellationToken);

        reloaded[euroAlert.Id].Should().Be("EUR");
        reloaded[poundAlert.Id].Should().Be("GBP");
    }

    [Fact]
    public async Task Backfill_LeavesNoAlertDenominatedInEmptyString()
    {
        // The regression that matters on deploy day: any row still holding "" after the migration is
        // an alert that will never fire again, because no product is priced in "".
        await using var ctx = fixture.CreateContext();

        var user = TestEntityFactory.User().Build();
        ctx.Users.Add(user);
        var product = NewProduct(user.Id, "USD");
        ctx.Products.Add(product);
        var alert = NewAlert(product, user.Id);
        ctx.Alerts.Add(alert);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        await ctx.Database.ExecuteSqlRawAsync(
            """UPDATE "Alerts" SET "Currency" = '' WHERE "Id" = {0}""",
            [alert.Id],
            TestContext.Current.CancellationToken);

        await ctx.Database.ExecuteSqlRawAsync(
            AddAlertCurrency.BackfillSql, TestContext.Current.CancellationToken);

        // Scoped to this test's own row: the fixture database is shared across the Postgres
        // collection, so a table-wide count would be reporting on other tests' leftovers.
        var blanks = await ctx.Alerts.AsNoTracking()
            .CountAsync(a => a.Id == alert.Id && a.Currency == "", TestContext.Current.CancellationToken);
        blanks.Should().Be(0);
    }

    [Fact]
    public async Task BackfilledAlert_IsNotDormantAgainstItsOwnProduct()
    {
        // Ties the data migration to the behaviour it exists to protect: a pre-existing alert must
        // still be evaluable after the upgrade, not quietly dormant.
        await using var ctx = fixture.CreateContext();

        var user = TestEntityFactory.User().Build();
        ctx.Users.Add(user);
        var product = NewProduct(user.Id, "EUR");
        ctx.Products.Add(product);
        var alert = NewAlert(product, user.Id);
        ctx.Alerts.Add(alert);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        await ctx.Database.ExecuteSqlRawAsync(
            """UPDATE "Alerts" SET "Currency" = '' WHERE "Id" = {0}""",
            [alert.Id],
            TestContext.Current.CancellationToken);
        await ctx.Database.ExecuteSqlRawAsync(
            AddAlertCurrency.BackfillSql, TestContext.Current.CancellationToken);

        var reloaded = await ctx.Alerts.AsNoTracking()
            .FirstAsync(a => a.Id == alert.Id, TestContext.Current.CancellationToken);

        reloaded.HasCurrencyMismatch(product.Currency).Should().BeFalse();
        reloaded.ShouldTrigger(40m, product.Currency).Should().BeTrue();
    }

    private static Product NewProduct(Guid userId, string currency) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Name = $"Product {currency}",
        CurrentPrice = 50m,
        Currency = currency,
        Status = ProductStatus.Active
    };

    private static Alert NewAlert(Product product, Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        ProductId = product.Id,
        UserId = userId,
        TargetPrice = 45m,
        ReferencePrice = 50m,
        Condition = AlertCondition.Below,
        IsActive = true
    };
}
