using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Baskets;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Baskets;

/// <summary>GetBasketHistory's wiring (#150): the window, the held-start and "now" rules of the product
/// history, the complete stores of now, and the user's shipping rates. The rules are BasketHistory's.</summary>
public class GetBasketHistoryTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _db;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Now));
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();

    public GetBasketHistoryTests()
    {
        (_db, _connection) = TestDbContextFactory.Create();
        _db.Users.Add(TestEntityFactory.User(_userId).Build());
        _db.Users.Add(TestEntityFactory.User(_otherUserId).Build());
        _db.SaveChanges();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Product AddProduct(string name)
    {
        var product = new Product { Id = Guid.NewGuid(), UserId = _userId, Name = name, Currency = "EUR" };
        _db.Products.Add(product);
        return product;
    }

    private ProductUrl AddUrl(Product product, string host, decimal currentPrice, ProductUrlStatus status = ProductUrlStatus.Active,
        params (DateTime At, decimal Price)[] points)
    {
        var url = new ProductUrl
        {
            Id = Guid.NewGuid(), ProductId = product.Id, Url = $"https://www.{host}/{Guid.NewGuid():N}",
            CurrentPrice = currentPrice, Currency = "EUR", Status = status
        };
        _db.ProductUrls.Add(url);
        foreach (var (at, price) in points)
        {
            _db.PricePoints.Add(new PricePoint
            {
                Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = url.Id, Price = price, Currency = "EUR", RecordedAt = at
            });
        }
        return url;
    }

    private async Task<Guid> BasketWith(params (Product Product, int Quantity)[] items)
    {
        var basket = new Basket { Id = Guid.NewGuid(), UserId = _userId, Name = "History" };
        foreach (var (product, quantity) in items) basket.AddItem(product.Id, quantity);
        _db.Baskets.Add(basket);
        await _db.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();
        return basket.Id;
    }

    private Task<BasketHistoryDto> Get(Guid basketId, int days = 30, Guid? userId = null) =>
        new GetBasketHistory.Handler(_db, _time).Handle(new GetBasketHistory.Query(basketId, userId ?? _userId, days), Ct);

    [Fact]
    public async Task GetBasketHistory_PriceFromBeforeTheWindow_StartsAtTheWindowStartAndRunsToNow()
    {
        var mouse = AddProduct("Mouse");
        AddUrl(mouse, "one.example", 8m, points: [(Now.AddDays(-60), 10m), (Now.AddDays(-5), 8m)]);
        _db.ShippingRates.Add(new ShippingRate { Id = Guid.NewGuid(), UserId = _userId, Host = "one.example", Amount = 4m, Currency = "EUR" });
        var basketId = await BasketWith((mouse, 2));

        var result = await Get(basketId, days: 30);

        var start = Now.Date.AddDays(-30);
        result.Days.Should().Be(30);
        result.Series.Should().ContainSingle();
        result.Series[0].Host.Should().Be("one.example");
        result.Series[0].Currency.Should().Be("EUR");
        result.Series[0].Points.Should().Equal(
            new BasketHistoryPointDto(start, 24m),
            new BasketHistoryPointDto(Now.AddDays(-5), 20m),
            new BasketHistoryPointDto(Now, 20m));
    }

    [Fact]
    public async Task GetBasketHistory_IncompleteStoreNow_HasNoSeries()
    {
        var mouse = AddProduct("Mouse");
        var pad = AddProduct("Pad");
        AddUrl(mouse, "one.example", 10m, points: [(Now.AddDays(-3), 10m)]);
        AddUrl(pad, "one.example", 20m, points: [(Now.AddDays(-3), 20m)]);
        AddUrl(mouse, "two.example", 9m, points: [(Now.AddDays(-3), 9m)]);
        var basketId = await BasketWith((mouse, 1), (pad, 1));

        var result = await Get(basketId);

        result.Series.Select(s => s.Host).Should().Equal("one.example");
    }

    [Fact]
    public async Task GetBasketHistory_PausedUrlWithNoPointInTheWindow_TakesNoPart()
    {
        // The paused URL's price from before the window is not known to hold during it.
        var mouse = AddProduct("Mouse");
        AddUrl(mouse, "one.example", 5m, ProductUrlStatus.Paused, (Now.AddDays(-60), 5m));
        AddUrl(mouse, "one.example", 10m, points: [(Now.AddDays(-60), 10m)]);
        var basketId = await BasketWith((mouse, 1));

        var result = await Get(basketId);

        result.Series.Single().Points.Should().Equal(
            new BasketHistoryPointDto(Now.Date.AddDays(-30), 10m),
            new BasketHistoryPointDto(Now, 10m));
    }

    [Fact]
    public async Task GetBasketHistory_AnotherUsersBasket_ThrowsNotFound()
    {
        var basketId = await BasketWith();

        var act = () => Get(basketId, userId: _otherUserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
