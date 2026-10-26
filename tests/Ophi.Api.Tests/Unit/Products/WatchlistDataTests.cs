using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Api.Features.Products;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Products;

/// <summary>Watchlist data (#152): the carried sparkline and range, the 7-day change and the real low.</summary>
public class WatchlistDataTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly GetProducts.Handler _handler;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTime _now = DateTime.UtcNow;

    public WatchlistDataTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new GetProducts.Handler(_dbContext, TimeProvider.System, NullLogger<GetProducts.Handler>.Instance);
        _dbContext.Users.Add(new User { Id = _userId, Email = "watch@example.com", Name = "Watch", PasswordHash = "hash" });
        _dbContext.SaveChanges();
    }

    /// <summary>A URL with its change points as (days ago, price); its current price is the last one.</summary>
    private sealed record UrlSpec(params (int DaysAgo, decimal Price)[] Points)
    {
        public ProductUrlStatus Status { get; init; } = ProductUrlStatus.Active;
        public string Currency { get; init; } = "EUR";
    }

    private Product AddProduct(string name, params UrlSpec[] urls)
    {
        var product = new Product { Id = Guid.NewGuid(), UserId = _userId, Name = name, Currency = urls[0].Currency };
        _dbContext.Products.Add(product);
        foreach (var spec in urls)
        {
            var url = new ProductUrl
            {
                Id = Guid.NewGuid(), ProductId = product.Id, Url = $"https://{Guid.NewGuid():N}.example/p",
                Currency = spec.Currency, Status = spec.Status,
                CurrentPrice = spec.Points.Length > 0 ? spec.Points[^1].Price : null
            };
            _dbContext.ProductUrls.Add(url);
            product.ProductUrls.Add(url);
            foreach (var (daysAgo, price) in spec.Points)
            {
                _dbContext.PricePoints.Add(new PricePoint
                {
                    Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = url.Id, Price = price,
                    Currency = spec.Currency, RecordedAt = _now.Date.AddDays(-daysAgo).AddMinutes(30)
                });
            }
        }
        product.CurrentPrice = product.ProductUrls
            .Where(u => u.Status != ProductUrlStatus.Paused && u.CurrentPrice != null)
            .Min(u => u.CurrentPrice);
        _dbContext.SaveChanges();
        return product;
    }

    private Task<GetProducts.Response> List(GetProducts.Query? query = null) =>
        _handler.Handle(query ?? new GetProducts.Query(_userId, IncludeSparkline: true), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Handle_StablePriceFor40Days_GivesThirtyFlatSparklineValues()
    {
        AddProduct("Stable", new UrlSpec((40, 20m)));

        var item = (await List()).Items.Single();

        item.Sparkline.Should().HaveCount(GetProducts.SparklineDays);
        item.Sparkline!.Select(s => s.Price).Should().AllBeEquivalentTo(20m);
        item.PriceMin.Should().Be(20m);
        item.PriceMax.Should().Be(20m);
    }

    [Fact]
    public async Task Handle_TwoUrlsChangingOnDifferentDays_SparklineIsTheMinOfTheCarriedPrices()
    {
        AddProduct("Two stores", new UrlSpec((5, 30m), (2, 25m)), new UrlSpec((4, 28m), (1, 35m)));

        var item = (await List()).Items.Single();

        item.Sparkline!.Select(s => s.Price).Should().Equal(30m, 28m, 28m, 25m, 25m, 25m);
    }

    [Fact]
    public async Task Handle_PriceHeldIntoTheRangeWindow_CountsInTheRange()
    {
        AddProduct("Held", new UrlSpec((100, 10m), (60, 40m), (10, 100m)));

        var item = (await List()).Items.Single();

        item.PriceMin.Should().Be(10m);
        item.PriceMax.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_ANewCheaperUrl_IsNoChange()
    {
        AddProduct("New store", new UrlSpec((20, 20m)), new UrlSpec((2, 15m)));

        var result = await List();

        result.Items.Single().Change7d.Should().Be(0m);
        result.PriceDropCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ANewCheaperUrl_IsANewCheapestStore()
    {
        AddProduct("New store", new UrlSpec((20, 20m)), new UrlSpec((2, 15m)));

        (await List()).Items.Single().NewCheapestStore.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AnOldUrlThatDroppedItsPrice_IsNotANewCheapestStore()
    {
        AddProduct("Old store", new UrlSpec((20, 20m), (2, 15m)), new UrlSpec((3, 18m)));

        (await List()).Items.Single().NewCheapestStore.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AProductYoungerThanSevenDays_IsNotANewCheapestStore()
    {
        AddProduct("Young", new UrlSpec((5, 20m)), new UrlSpec((2, 15m)));

        (await List()).Items.Single().NewCheapestStore.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AUrlThatDroppedItsPrice_IsADrop()
    {
        AddProduct("Dropped", new UrlSpec((20, 20m), (2, 15m)));

        var result = await List();

        result.Items.Single().Change7d.Should().Be(-25m);
        result.PriceDropCount.Should().Be(1);
        (await List(new GetProducts.Query(_userId, PriceDrop: true))).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_APausedUrl_TakesNoPartInTheChange()
    {
        // b was the cheapest 7 days ago but is paused now: comparing with it would show a rise.
        AddProduct("Paused store", new UrlSpec((20, 20m), (2, 18m)), new UrlSpec((20, 10m)) { Status = ProductUrlStatus.Paused });

        var item = (await List()).Items.Single();

        item.Change7d.Should().Be(-10m);
    }

    [Fact]
    public async Task Handle_FiveDaysOfHistory_HasNoChange()
    {
        AddProduct("Young", new UrlSpec((5, 20m), (1, 15m)));

        var result = await List();

        var item = result.Items.Single();
        item.Change7d.Should().BeNull();
        item.TrackedSince.Should().Be(_now.Date.AddDays(-5).AddMinutes(30));
        result.PriceDropCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_PriceChangeSort_UsesTheSevenDayChange()
    {
        AddProduct("Small drop", new UrlSpec((20, 100m), (2, 90m)));
        AddProduct("Rise", new UrlSpec((20, 100m), (2, 150m)));
        AddProduct("Big drop", new UrlSpec((20, 100m), (2, 50m)));

        var result = await List(new GetProducts.Query(_userId, SortBy: "pricechange", SortDirection: "asc"));

        result.Items.Select(i => i.Name).Should().Equal("Big drop", "Small drop", "Rise");
    }

    [Fact]
    public async Task Handle_RealLow_NeedsThirtyDaysAndTwoDifferentPrices()
    {
        AddProduct("Ten days at its low", new UrlSpec((10, 30m), (2, 20m)));
        AddProduct("Stable", new UrlSpec((40, 20m)));
        AddProduct("Forty days at its low", new UrlSpec((40, 30m), (2, 20m)));
        AddProduct("Forty days above its low", new UrlSpec((40, 20m), (2, 30m)));

        var all = await List();
        var filtered = await List(new GetProducts.Query(_userId, AtLowest: true));

        filtered.Items.Select(i => i.Name).Should().Equal("Forty days at its low");
        all.AtLowestCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_RealLow_CountsALowerPriceHeldIntoTheWindow()
    {
        // 15 was the price 90 days ago (set 120 days ago), so 18 is not a 90-day low.
        AddProduct("Held low", new UrlSpec((120, 15m), (60, 25m), (2, 18m)));

        var all = await List();

        all.AtLowestCount.Should().Be(0);
        all.Items.Single().PriceMin.Should().Be(15m);
    }

    [Fact]
    public async Task Handle_PriceSort_OrdersByCurrencyThenPrice()
    {
        AddProduct("USD 25.99", new UrlSpec((3, 25.99m)) { Currency = "USD" });
        AddProduct("EUR 25.99", new UrlSpec((3, 25.99m)));
        AddProduct("USD 5", new UrlSpec((3, 5m)) { Currency = "USD" });
        AddProduct("EUR 10", new UrlSpec((3, 10m)));

        var asc = await List(new GetProducts.Query(_userId, SortBy: "price", SortDirection: "asc"));
        var desc = await List(new GetProducts.Query(_userId, SortBy: "price", SortDirection: "desc"));

        asc.Items.Select(i => i.Name).Should().Equal("EUR 10", "EUR 25.99", "USD 5", "USD 25.99");
        desc.Items.Select(i => i.Name).Should().Equal("EUR 25.99", "EUR 10", "USD 25.99", "USD 5");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
