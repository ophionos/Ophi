using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Api.Features.Baskets;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Baskets;

/// <summary>GetBasket's per-store totals, best store and split (#148). The rules are BasketTotals'; these
/// tests pin the wiring: the user's own shipping rates, the live URLs, and the DTO shape.</summary>
public class BasketTotalsHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _db;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();

    public BasketTotalsHandlerTests()
    {
        (_db, _connection) = TestDbContextFactory.Create();
        _db.Users.Add(TestEntityFactory.User(_userId).Build());
        _db.Users.Add(TestEntityFactory.User(_otherUserId).Build());
        _db.SaveChanges();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Product AddProduct(string name, params (string Host, decimal Price, bool OutOfStock)[] urls)
    {
        var product = new Product { Id = Guid.NewGuid(), UserId = _userId, Name = name, Currency = "EUR" };
        _db.Products.Add(product);
        foreach (var (host, price, outOfStock) in urls)
        {
            _db.ProductUrls.Add(new ProductUrl
            {
                Id = Guid.NewGuid(), ProductId = product.Id, Url = $"https://www.{host}/{Guid.NewGuid():N}",
                CurrentPrice = price, Currency = "EUR", Status = ProductUrlStatus.Active, IsOutOfStock = outOfStock
            });
        }
        return product;
    }

    private async Task<Guid> BasketWith(params (Product Product, int Quantity)[] items)
    {
        var basket = new Basket { Id = Guid.NewGuid(), UserId = _userId, Name = "Setup" };
        foreach (var (product, quantity) in items) basket.AddItem(product.Id, quantity);
        _db.Baskets.Add(basket);
        await _db.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();
        return basket.Id;
    }

    private Task<BasketDto> Get(Guid basketId) =>
        new GetBasket.Handler(_db).Handle(new GetBasket.Query(basketId, _userId), Ct);

    [Fact]
    public async Task GetBasket_WithPricedItems_ReturnsStoreTotalsWithTheUsersShipping()
    {
        var mouse = AddProduct("Mouse", ("one.example", 10m, false), ("two.example", 12m, false));
        var pad = AddProduct("Pad", ("one.example", 20m, false));
        _db.ShippingRates.Add(new ShippingRate { Id = Guid.NewGuid(), UserId = _userId, Host = "one.example", Amount = 4m, Currency = "EUR" });
        // Another user's rate for the same host must not apply.
        _db.ShippingRates.Add(new ShippingRate { Id = Guid.NewGuid(), UserId = _otherUserId, Host = "two.example", Amount = 99m, Currency = "EUR" });
        var basketId = await BasketWith((mouse, 2), (pad, 1));

        var result = await Get(basketId);

        var one = result.Stores.Single(s => s.Host == "one.example");
        one.Should().BeEquivalentTo(new
        {
            Currency = "EUR", Subtotal = 40m, Shipping = 4m, Total = 44m, Complete = true, MissingCount = 0
        });
        one.Items.Should().Equal(
            new BasketStoreItemDto(mouse.Id, 10m, "EUR"),
            new BasketStoreItemDto(pad.Id, 20m, "EUR"));

        var two = result.Stores.Single(s => s.Host == "two.example");
        two.Complete.Should().BeFalse();
        two.MissingCount.Should().Be(1);
        two.Shipping.Should().Be(0m);

        result.BestStore.Should().Be("one.example");
        result.Split!.Total.Should().Be(44m);
        result.Split.Lines.Should().Equal(
            new BasketSplitLineDto(mouse.Id, "one.example", 10m),
            new BasketSplitLineDto(pad.Id, "one.example", 20m));
        result.Split.Shipping.Should().Equal(new BasketHostShippingDto("one.example", 4m));
    }

    [Fact]
    public async Task GetBasket_OutOfStockUrl_IsNotAnOffer()
    {
        var mouse = AddProduct("Mouse", ("one.example", 10m, true), ("two.example", 12m, false));
        var basketId = await BasketWith((mouse, 1));

        var result = await Get(basketId);

        result.Stores.Select(s => s.Host).Should().Equal("two.example");
        result.BestStore.Should().Be("two.example");
    }

    [Fact]
    public async Task GetBasket_Empty_HasNoStoresAndNoSplit()
    {
        var basketId = await BasketWith();

        var result = await Get(basketId);

        result.Stores.Should().BeEmpty();
        result.BestStore.Should().BeNull();
        result.Split.Should().BeNull();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
