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
    public async Task GetComparisonGroups_ReturnsTheBestPriceAndItsCurrencyMatchedDailyLows()
    {
        // The card preview filters points on the product's currency through a join and groups by
        // UTC day; both only run for real on Npgsql (timestamptz, case-sensitive text compare).
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        Product best;

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(userId).Build());
            ctx.ComparisonGroups.Add(new ComparisonGroup { Id = groupId, UserId = userId, Name = "Desks" });
            best = TestEntityFactory.Product(userId).Named("Cheap").InGroup(groupId).Priced(40m).Build();
            ctx.Products.AddRange(
                best,
                TestEntityFactory.Product(userId).Named("Dear").InGroup(groupId).Priced(60m).Build(),
                TestEntityFactory.Product(userId).Named("Unpriced").InGroup(groupId).Build());
            ctx.PricePoints.AddRange(
                new PricePoint { Id = Guid.NewGuid(), ProductId = best.Id, Price = 45m, Currency = "USD", RecordedAt = now.AddDays(-3) },
                new PricePoint { Id = Guid.NewGuid(), ProductId = best.Id, Price = 30m, Currency = "EUR", RecordedAt = now.AddDays(-2) },
                new PricePoint { Id = Guid.NewGuid(), ProductId = best.Id, Price = 40m, Currency = "USD", RecordedAt = now.AddDays(-1) });
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var handler = new Ophi.Api.Features.Comparisons.GetComparisonGroups.Handler(
                ctx, TimeProvider.System,
                NullLogger<Ophi.Api.Features.Comparisons.GetComparisonGroups.Handler>.Instance);

            var result = await handler.Handle(
                new Ophi.Api.Features.Comparisons.GetComparisonGroups.Query(userId),
                TestContext.Current.CancellationToken);

            var dto = result.Items.Should().ContainSingle().Subject;
            dto.ProductCount.Should().Be(3);
            dto.BestPriceProductId.Should().Be(best.Id);
            dto.BestPrice.Should().Be(40m);
            dto.Sparkline.Select(s => s.Price).Should().Equal(45m, 40m);
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

    [Fact]
    public async Task GetProducts_NeedsActionAndNearTarget_TranslateAndCountOnPostgres()
    {
        // Both predicates run as SQL only for real here: the stored error category (an int column,
        // null on legacy rows) and the currency compare, which Postgres does case-sensitively.
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(userId).Build());

            var blocked = TestEntityFactory.Product(userId).Named("Blocked").Build();
            var blockedUrl = TestEntityFactory.ProductUrl(blocked.Id).Build();
            blockedUrl.RecordFailure("Blocked by anti-bot", now, ScrapeErrorCategory.AntiBot);

            // A row written before the category column existed: an error, no category.
            var legacy = TestEntityFactory.Product(userId).Named("Legacy").Build();
            var legacyUrl = new ProductUrl
            {
                Id = Guid.NewGuid(), ProductId = legacy.Id, Url = "https://example.com/legacy",
                LastError = "HTTP 500", ConsecutiveFailures = 3
            };

            var rateLimited = TestEntityFactory.Product(userId).Named("Rate limited").Build();
            var rateLimitedUrl = TestEntityFactory.ProductUrl(rateLimited.Id).Build();
            for (var i = 0; i < 3; i++) rateLimitedUrl.RecordTransientFailure("429", now);

            var near = TestEntityFactory.Product(userId).Named("Near").Priced(104m).Build();
            var far = TestEntityFactory.Product(userId).Named("Far").Priced(120m).Build();

            ctx.Products.AddRange(blocked, legacy, rateLimited, near, far);
            ctx.ProductUrls.AddRange(blockedUrl, legacyUrl, rateLimitedUrl);
            ctx.Alerts.AddRange(
                // Lower-case currency: a plain text compare would miss it on Postgres.
                TestEntityFactory.Alert(near.Id, userId).WithTarget(100m).WithCurrency("usd").Build(),
                TestEntityFactory.Alert(far.Id, userId).WithTarget(100m).Build());
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var handler = new Ophi.Api.Features.Products.GetProducts.Handler(
                ctx, TimeProvider.System, NullLogger<Ophi.Api.Features.Products.GetProducts.Handler>.Instance);

            var all = await handler.Handle(
                new Ophi.Api.Features.Products.GetProducts.Query(userId), TestContext.Current.CancellationToken);
            var needsAction = await handler.Handle(
                new Ophi.Api.Features.Products.GetProducts.Query(userId, NeedsAction: true),
                TestContext.Current.CancellationToken);
            var nearTarget = await handler.Handle(
                new Ophi.Api.Features.Products.GetProducts.Query(userId, NearTarget: true),
                TestContext.Current.CancellationToken);

            all.NeedsActionCount.Should().Be(2);
            needsAction.Items.Select(i => i.Name).Should().BeEquivalentTo("Blocked", "Legacy");
            all.Items.Single(i => i.Name == "Blocked").Attention!.Kind.Should().Be("blocked");
            all.Items.Single(i => i.Name == "Legacy").Attention!.Kind.Should().Be("failing");
            all.NearTargetCount.Should().Be(1);
            nearTarget.Items.Select(i => i.Name).Should().Equal("Near");
        }
    }

    [Fact]
    public async Task GetProducts_RangeLowestStoresAndCounts_TranslateAndRunOnPostgres()
    {
        // The 90-day range reads each URL's latest point before a timestamptz cutoff, "at lowest" a
        // correlated MIN over the same window, and the store list a DISTINCT over a join: none of it
        // runs for real on SQLite.
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(userId).Build());
            var product = TestEntityFactory.Product(userId).Named("Lamp").Priced(50m).Build();
            var url = new ProductUrl
            {
                Id = Guid.NewGuid(), ProductId = product.Id, Url = "https://shop.example/lamp", StoreId = "shop"
            };
            url.RecordSuccessfulScrape(50m, "USD", now);
            ctx.Products.Add(product);
            ctx.ProductUrls.Add(url);
            ctx.StoreConfigurations.Add(new StoreConfiguration
            {
                Id = Guid.NewGuid(), UserId = userId, StoreId = "shop", Name = "The Shop"
            });
            ctx.PricePoints.AddRange(
                new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = url.Id, Price = 30m, Currency = "USD", RecordedAt = now.AddDays(-120) },
                // Replaces 30 before the window opens, so 70 is the price held at its start.
                new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = url.Id, Price = 70m, Currency = "USD", RecordedAt = now.AddDays(-100) },
                new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = url.Id, Price = 80m, Currency = "USD", RecordedAt = now.AddDays(-60) },
                new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = url.Id, Price = 50m, Currency = "USD", RecordedAt = now.AddDays(-5) });
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var handler = new Ophi.Api.Features.Products.GetProducts.Handler(
                ctx, TimeProvider.System, NullLogger<Ophi.Api.Features.Products.GetProducts.Handler>.Instance);

            var all = await handler.Handle(
                new Ophi.Api.Features.Products.GetProducts.Query(userId, IncludeSparkline: true),
                TestContext.Current.CancellationToken);
            var atStore = await handler.Handle(
                new Ophi.Api.Features.Products.GetProducts.Query(userId, AtLowest: true, StoreId: "shop", SortBy: "favourites"),
                TestContext.Current.CancellationToken);

            var item = all.Items.Should().ContainSingle().Subject;
            item.PriceMin.Should().Be(50m);
            item.PriceMax.Should().Be(80m);
            item.Sparkline.Should().HaveCount(Ophi.Api.Features.Products.GetProducts.SparklineDays);
            item.Sparkline![0].Price.Should().Be(80m);
            item.Sparkline[^1].Price.Should().Be(50m);
            item.BestStoreName.Should().Be("The Shop");
            item.LastSucceededAt.Should().NotBeNull();
            all.AtLowestCount.Should().Be(1);
            all.ProductCount.Should().Be(1);
            all.UrlCount.Should().Be(1);
            all.Stores.Should().Equal(new Ophi.Api.Features.Products.GetProducts.StoreOptionDto("shop", "The Shop"));
            atStore.Items.Should().ContainSingle();
        }
    }
}
