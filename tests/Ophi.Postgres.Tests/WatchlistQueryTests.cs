using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Api.Features.Products;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Postgres.Tests;

/// <summary>
/// The watchlist's 7-day change and real low (#152) are correlated subqueries in SQL. They must
/// translate on Postgres and give the same rows as the values the list reports.
/// </summary>
[Collection("Postgres")]
public class WatchlistQueryTests(PostgresFixture fixture)
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTime _now = DateTime.UtcNow;

    private void AddProduct(OphiDbContext ctx, string name, (int DaysAgo, decimal Price)[] a, (int DaysAgo, decimal Price)[]? b = null,
        ProductUrlStatus bStatus = ProductUrlStatus.Active)
    {
        var product = TestEntityFactory.Product(_userId).Named(name).WithCurrency("EUR").Build();
        ctx.Products.Add(product);
        foreach (var (points, status) in new[] { (a, ProductUrlStatus.Active), (b, bStatus) })
        {
            if (points is null) continue;
            var url = new ProductUrl
            {
                Id = Guid.NewGuid(), ProductId = product.Id, Url = $"https://{Guid.NewGuid():N}.example/p",
                Currency = "EUR", Status = status, CurrentPrice = points[^1].Price
            };
            ctx.ProductUrls.Add(url);
            product.ProductUrls.Add(url);
            foreach (var (daysAgo, price) in points)
            {
                ctx.PricePoints.Add(new PricePoint
                {
                    Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = url.Id, Price = price,
                    Currency = "EUR", RecordedAt = _now.Date.AddDays(-daysAgo).AddMinutes(30)
                });
            }
        }
        product.CurrentPrice = product.ProductUrls.Where(u => u.Status != ProductUrlStatus.Paused).Min(u => u.CurrentPrice);
    }

    private async Task<GetProducts.Response> List(GetProducts.Query query)
    {
        await using var ctx = fixture.CreateContext();
        return await new GetProducts.Handler(ctx, TimeProvider.System, NullLogger<GetProducts.Handler>.Instance)
            .Handle(query, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Change7d_FilterCountAndSort_AgreeWithTheReportedValues()
    {
        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(_userId).Build());
            AddProduct(ctx, "Big drop", [(20, 100m), (2, 50m)]);
            AddProduct(ctx, "Small drop", [(20, 100m), (2, 90m)]);
            AddProduct(ctx, "Rise", [(20, 100m), (2, 150m)]);
            AddProduct(ctx, "New cheaper store", [(20, 20m)], [(2, 15m)]);
            AddProduct(ctx, "Paused cheaper store", [(20, 20m), (2, 18m)], [(20, 10m)], ProductUrlStatus.Paused);
            AddProduct(ctx, "Young", [(5, 20m), (1, 15m)]);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var all = await List(new GetProducts.Query(_userId, PageSize: 50));
        var drops = await List(new GetProducts.Query(_userId, PriceDrop: true, PageSize: 50));
        var sorted = await List(new GetProducts.Query(_userId, SortBy: "pricechange", SortDirection: "asc", PageSize: 50));

        all.Items.ToDictionary(i => i.Name, i => i.Change7d).Should().BeEquivalentTo(new Dictionary<string, decimal?>
        {
            ["Big drop"] = -50m,
            ["Small drop"] = -10m,
            ["Rise"] = 50m,
            ["New cheaper store"] = 0m,
            ["Paused cheaper store"] = -10m,
            ["Young"] = null
        });
        var expectedDrops = all.Items.Where(i => i.Change7d < 0).Select(i => i.Name).ToList();
        drops.Items.Select(i => i.Name).Should().BeEquivalentTo(expectedDrops);
        all.PriceDropCount.Should().Be(expectedDrops.Count);
        // Postgres sorts NULL last on ascending order.
        sorted.Items.Select(i => i.Name).Take(5).Should().Equal(
            all.Items.Where(i => i.Change7d != null).OrderBy(i => i.Change7d).ThenBy(i => i.Id).Select(i => i.Name));
    }

    [Fact]
    public async Task RealLow_FilterAndCount_ApplyTheHistoryGuard()
    {
        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Add(TestEntityFactory.User(_userId).Build());
            AddProduct(ctx, "Ten days at its low", [(10, 30m), (2, 20m)]);
            AddProduct(ctx, "Stable", [(40, 20m)]);
            AddProduct(ctx, "Forty days at its low", [(40, 30m), (2, 20m)]);
            AddProduct(ctx, "Held low", [(120, 15m), (60, 25m), (2, 18m)]);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var all = await List(new GetProducts.Query(_userId, IncludeSparkline: true, PageSize: 50));
        var lows = await List(new GetProducts.Query(_userId, AtLowest: true, PageSize: 50));

        lows.Items.Select(i => i.Name).Should().Equal("Forty days at its low");
        all.AtLowestCount.Should().Be(1);
        all.Items.Single(i => i.Name == "Held low").PriceMin.Should().Be(15m);
        all.Items.Single(i => i.Name == "Stable").Sparkline.Should().HaveCount(GetProducts.SparklineDays);
    }
}
