using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.TestHelpers;
using Ophi.Worker.Services;

namespace Ophi.Postgres.Tests;

/// <summary>
/// Phase 1 provider-sensitive coverage that the SQLite unit tier cannot give (SQLite's type affinity
/// silently accepts Postgres type names, so a SQLite "smoke" test is a false green). Each test below
/// exercises a behaviour that only differs on real Postgres: migration apply, jsonb, Npgsql UTC
/// strictness, and case-sensitive LIKE.
/// </summary>
[Collection("Postgres")]
public class ProviderSensitiveTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Migrations_ApplyCleanly_AndModelMatchesSnapshot()
    {
        // The fixture already ran MigrateAsync(); these assertions confirm it took and that the
        // regenerated Postgres baseline matches the current model (the check that false-greened
        // on SQLite via PendingModelChangesWarning).
        await using var ctx = fixture.CreateContext();

        var applied = await ctx.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        applied.Should().NotBeEmpty("the InitialPostgres baseline must be applied");

        var pending = await ctx.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);
        pending.Should().BeEmpty("the Postgres baseline must be in sync with the EF model");
    }

    [Fact]
    public async Task CustomFields_Jsonb_RoundTrips()
    {
        var userId = Guid.NewGuid();

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(userId).Build());
            var product = TestEntityFactory.Product(userId).Named("Widget").Build();
            product.CustomFields = [new CustomField("Color", "Red"), new CustomField("Size", "L")];
            ctx.Products.Add(product);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var product = await ctx.Products.SingleAsync(p => p.UserId == userId, TestContext.Current.CancellationToken);
            product.CustomFields.Should().HaveCount(2);
            product.CustomFields.Should().ContainEquivalentOf(new CustomField("Color", "Red"));
            product.CustomFields.Should().ContainEquivalentOf(new CustomField("Size", "L"));
        }
    }

    [Fact]
    public async Task DateTime_WithUnspecifiedKind_IsNormalizedToUtc()
    {
        var userId = Guid.NewGuid();
        // An Unspecified-kind value would throw on an Npgsql timestamptz write without the UTC
        // convention. The successful save + Utc read back is the proof the converter works.
        var unspecified = new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Unspecified);

        Guid urlId;
        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(userId).Build());
            var product = TestEntityFactory.Product(userId).Build();
            var url = TestEntityFactory.ProductUrl(product.Id).LastCheckedAt(unspecified).Build();
            urlId = url.Id;
            ctx.Products.Add(product);
            ctx.ProductUrls.Add(url);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var url = await ctx.ProductUrls.SingleAsync(u => u.Id == urlId, TestContext.Current.CancellationToken);
            url.LastCheckedAt.Should().NotBeNull();
            url.LastCheckedAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
            url.LastCheckedAt!.Value.Should().Be(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc));
        }
    }

    [Fact]
    public async Task Search_LowerLike_IsCaseInsensitive_OnPostgres()
    {
        var userId = Guid.NewGuid();

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(userId).Build());
            ctx.Products.Add(TestEntityFactory.Product(userId).Named("Sony WH-1000XM5").Build());
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var ctx = fixture.CreateContext())
        {
            // Mirrors the GetProducts/GetTags fix: lower() both sides + LIKE. Plain Postgres LIKE is
            // case-sensitive, so this uppercase term only matches because of the lower()+LIKE form.
            var pattern = $"%{"SONY".ToLower()}%";
            var results = await ctx.Products
                .Where(p => p.UserId == userId && EF.Functions.Like(p.Name.ToLower(), pattern))
                .ToListAsync(TestContext.Current.CancellationToken);

            results.Should().ContainSingle().Which.Name.Should().Be("Sony WH-1000XM5");
        }
    }

    [Fact]
    public async Task Dispatcher_DueQuery_TranslatesAndRuns_OnPostgres()
    {
        // The Worker's PriceCheckDispatcher filters/orders on LastCheckedAt server-side. That's the
        // one place a server-side DateTime comparison + OrderBy meets the global UTC value converter,
        // and it's worker-only (the API smoke didn't touch it). Proves the query translates and runs
        // on Npgsql rather than throwing "could not be translated".
        var userId = Guid.NewGuid();
        Guid dueUrlId;

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(userId).Build());
            var product = TestEntityFactory.Product(userId).WithStatus(ProductStatus.Active).Build();
            var url = TestEntityFactory.ProductUrl(product.Id)
                .LastCheckedAt(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)) // long overdue
                .Build();
            dueUrlId = url.Id;
            ctx.Products.Add(product);
            ctx.ProductUrls.Add(url);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var due = await PriceCheckDispatcher.SelectDueProductUrlIdsAsync(
                ctx.ProductUrls, DateTime.UtcNow, batchSize: 50, TestContext.Current.CancellationToken);

            due.Should().Contain(dueUrlId);
        }
    }

    [Fact]
    public async Task GetPriceHistory_StableUrlCarryForward_TranslatesAndRuns_OnPostgres()
    {
        // The carry-forward lookup filters on a NULLABLE FK with `!= null` plus `.Contains(...)`
        // over a client-side Guid list, then orders by RecordedAt (which meets the global UTC value
        // converter). That shape is exactly what silently works on SQLite and throws "could not be
        // translated" on Npgsql, so a SQLite green is not evidence. Asserts the reconstructed series
        // too, not just that the query ran.
        var userId = Guid.NewGuid();
        Guid productId;

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(userId).Build());
            var product = TestEntityFactory.Product(userId).WithStatus(ProductStatus.Active).Build();
            productId = product.Id;
            var moving = TestEntityFactory.ProductUrl(product.Id).Build();
            var stable = TestEntityFactory.ProductUrl(product.Id).Build();
            ctx.Products.Add(product);
            ctx.ProductUrls.AddRange(moving, stable);
            ctx.PricePoints.AddRange(
                // Inside a 7-day window.
                new PricePoint
                {
                    Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = moving.Id,
                    Price = 294.78m, Currency = "EUR",
                    RecordedAt = DateTime.UtcNow.AddDays(-1)
                },
                // Outside it — the price this URL still holds.
                new PricePoint
                {
                    Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = stable.Id,
                    Price = 279.99m, Currency = "EUR",
                    RecordedAt = DateTime.UtcNow.AddDays(-55)
                });
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var handler = new Ophi.Api.Features.Products.GetPriceHistory.Handler(
                ctx, TimeProvider.System,
                NullLogger<Ophi.Api.Features.Products.GetPriceHistory.Handler>.Instance);

            var result = await handler.Handle(
                new Ophi.Api.Features.Products.GetPriceHistory.Query(productId, userId, Days: 7),
                TestContext.Current.CancellationToken);

            result.UrlHistories.Should().NotBeNull();
            result.UrlHistories!.Should().HaveCount(2);
            result.UrlHistories!.Should().AllSatisfy(uh =>
                uh.History.Should().HaveCountGreaterThanOrEqualTo(2,
                    "a stable store must still draw a line, not vanish from the chart"));
            result.UrlHistories!.Should().Contain(uh => uh.History.All(h => h.Price == 279.99m));
        }
    }

    [Fact]
    public async Task GetProducts_PagingThroughTiedPrices_YieldsEveryProductExactlyOnce()
    {
        // Every GetProducts sort ends on a non-unique column, so without a unique final key SQL
        // guarantees nothing about the order of tied rows — Skip/Take may then repeat one product
        // across pages while dropping another.
        //
        // Honest scope: this is a CONTRACT guard, not a reproduction. At this row count both
        // providers happen to be stable (Postgres seq-scans in physical order, SQLite scans the Id
        // primary-key index), so the test passes with or without the tiebreaker. Reproducing the
        // instability needs a plan change between the page queries — parallel or index scans at
        // volume, or concurrent updates moving rows. It lives in the Postgres tier because that is
        // where a realistic plan would ever be chosen, and it fails loudly if paging is ever
        // restructured in a way that actually drops or repeats rows.
        var userId = Guid.NewGuid();
        var expectedIds = new List<Guid>();

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(userId).Build());
            for (var i = 0; i < 6; i++)
            {
                var product = TestEntityFactory.Product(userId)
                    .Named($"Tied Product {i}")
                    .WithStatus(ProductStatus.Active)
                    .Build();
                product.CurrentPrice = 9.99m; // identical across every row
                expectedIds.Add(product.Id);
                ctx.Products.Add(product);
            }
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var seen = new List<Guid>();
        await using (var ctx = fixture.CreateContext())
        {
            var handler = new Ophi.Api.Features.Products.GetProducts.Handler(
                ctx, TimeProvider.System, NullLogger<Ophi.Api.Features.Products.GetProducts.Handler>.Instance);

            for (var page = 1; page <= 3; page++)
            {
                var result = await handler.Handle(
                    new Ophi.Api.Features.Products.GetProducts.Query(
                        userId, SortBy: "price", SortDirection: "asc", Page: page, PageSize: 2),
                    TestContext.Current.CancellationToken);
                seen.AddRange(result.Items.Select(i => i.Id));
            }
        }

        seen.Should().OnlyHaveUniqueItems("a tied row must not appear on two pages");
        seen.Should().BeEquivalentTo(expectedIds, "no row may be dropped between pages");
    }
}
