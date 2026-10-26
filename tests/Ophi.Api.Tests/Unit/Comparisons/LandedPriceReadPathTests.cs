using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Api.Features.Comparisons;
using Ophi.Api.Features.Products;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Comparisons;

/// <summary>Landed prices (#145) on the comparison detail, the comparison cards and the product page.</summary>
public class LandedPriceReadPathTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ComparisonGroup _group;

    public LandedPriceReadPathTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContext.Users.Add(new User { Id = _userId, Email = "landed@example.com", Name = "Landed", PasswordHash = "hash" });
        _group = new ComparisonGroup { Id = Guid.NewGuid(), UserId = _userId, Name = "Wheels" };
        _dbContext.ComparisonGroups.Add(_group);
        _dbContext.SaveChanges();
    }

    private Product AddProduct(string name, params (string Url, decimal? Price, ProductUrlStatus Status)[] urls)
    {
        var headline = urls.Where(u => u.Status != ProductUrlStatus.Paused && u.Price != null).Min(u => u.Price);
        var product = TestEntityFactory.Product(_userId).Named(name).Priced(headline).WithCurrency("EUR").Build();
        product.ComparisonGroupId = _group.Id;
        _dbContext.Products.Add(product);
        foreach (var (url, price, status) in urls)
        {
            _dbContext.ProductUrls.Add(new ProductUrl
            {
                Id = Guid.NewGuid(), ProductId = product.Id, Url = url, CurrentPrice = price, Currency = "EUR", Status = status
            });
        }
        _dbContext.SaveChanges();
        return product;
    }

    private void AddRate(string host, decimal amount, string currency = "EUR", decimal? freeAbove = null, Guid? userId = null)
    {
        _dbContext.ShippingRates.Add(new ShippingRate
        {
            Id = Guid.NewGuid(), UserId = userId ?? _userId, Host = host, Amount = amount, Currency = currency, FreeAbove = freeAbove
        });
        _dbContext.SaveChanges();
    }

    private Task<GetComparisonGroup.Response> Detail() =>
        new GetComparisonGroup.Handler(_dbContext, TimeProvider.System, NullLogger<GetComparisonGroup.Handler>.Instance)
            .Handle(new GetComparisonGroup.Query(_group.Id, _userId), TestContext.Current.CancellationToken);

    private async Task<GetComparisonGroups.Dto> Card() =>
        (await new GetComparisonGroups.Handler(_dbContext, TimeProvider.System, NullLogger<GetComparisonGroups.Handler>.Instance)
            .Handle(new GetComparisonGroups.Query(_userId), TestContext.Current.CancellationToken)).Items.Single();

    [Fact]
    public async Task GetComparisonGroup_FreeShippingStore_IsBestLandedButNotBestScraped()
    {
        var cheap = AddProduct("Cheap with shipping", ("https://a.example/1", 18.99m, ProductUrlStatus.Active));
        var dearer = AddProduct("Dearer, free shipping", ("https://b.example/1", 21m, ProductUrlStatus.Active));
        AddRate("a.example", 5m);

        var result = await Detail();

        result.BestPriceProductId.Should().Be(cheap.Id);
        result.BestLandedProductId.Should().Be(dearer.Id);
        result.BestLandedPrice.Should().Be(21m);
        var cheapDto = result.Products.Single(p => p.Id == cheap.Id);
        cheapDto.LandedPrice.Should().Be(23.99m);
        cheapDto.ShippingCost.Should().Be(5m);
        cheapDto.LandedHost.Should().Be("a.example");
        cheapDto.IsBestLandedPrice.Should().BeFalse();
        result.Products.Single(p => p.Id == dearer.Id).IsBestLandedPrice.Should().BeTrue();
    }

    [Fact]
    public async Task GetComparisonGroup_ShippingInAnotherCurrency_LeavesLandedUnknownAndNotBest()
    {
        var usdShipping = AddProduct("USD shipping", ("https://a.example/1", 10m, ProductUrlStatus.Active));
        var other = AddProduct("Other", ("https://b.example/1", 30m, ProductUrlStatus.Active));
        AddRate("a.example", 3m, currency: "USD");

        var result = await Detail();

        var dto = result.Products.Single(p => p.Id == usdShipping.Id);
        dto.LandedPrice.Should().BeNull();
        dto.ShippingCost.Should().BeNull();
        result.BestLandedProductId.Should().Be(other.Id);
    }

    [Fact]
    public async Task GetComparisonGroup_PausedUrl_DoesNotSetTheLandedPrice()
    {
        var product = AddProduct("Two stores",
            ("https://a.example/1", 5m, ProductUrlStatus.Paused),
            ("https://b.example/1", 20m, ProductUrlStatus.Active));

        var result = await Detail();

        var dto = result.Products.Single(p => p.Id == product.Id);
        dto.LandedPrice.Should().Be(20m);
        dto.LandedHost.Should().Be("b.example");
    }

    [Fact]
    public async Task GetComparisonGroup_IgnoresAnotherUsersRates()
    {
        var otherUser = Guid.NewGuid();
        _dbContext.Users.Add(new User { Id = otherUser, Email = "other@example.com", Name = "Other", PasswordHash = "hash" });
        _dbContext.SaveChanges();
        var product = AddProduct("Mine", ("https://a.example/1", 10m, ProductUrlStatus.Active));
        AddRate("a.example", 4m, userId: otherUser);

        var result = await Detail();

        result.Products.Single(p => p.Id == product.Id).LandedPrice.Should().Be(10m);
    }

    [Fact]
    public async Task GetComparisonGroups_CardNamesTheSameBestLandedProductAsTheDetail()
    {
        AddProduct("Cheap with shipping", ("https://a.example/1", 18.99m, ProductUrlStatus.Active));
        var dearer = AddProduct("Dearer, free shipping", ("https://b.example/1", 21m, ProductUrlStatus.Active));
        // Equal landed prices: the tie breaks on the product id in both places.
        var tie = AddProduct("Tie", ("https://c.example/1", 20m, ProductUrlStatus.Active));
        AddRate("a.example", 5m);
        AddRate("c.example", 1m);

        var card = await Card();
        var detail = await Detail();

        var expected = new[] { dearer.Id, tie.Id }.Min();
        detail.BestLandedProductId.Should().Be(expected);
        card.BestLandedProductId.Should().Be(expected);
        card.BestLandedPrice.Should().Be(21m);
        card.BestLandedProductName.Should().Be(expected == dearer.Id ? dearer.Name : tie.Name);
    }

    [Fact]
    public async Task GetProduct_ReportsShippingAndLandedPerUrlAndForTheProduct()
    {
        var product = AddProduct("Two stores",
            ("https://www.a.example/1", 18m, ProductUrlStatus.Active),
            ("https://b.example/1", 20m, ProductUrlStatus.Active));
        AddRate("a.example", 3m);

        var result = await new GetProduct.Handler(_dbContext, TimeProvider.System, NullLogger<GetProduct.Handler>.Instance)
            .Handle(new GetProduct.Query(product.Id, _userId), TestContext.Current.CancellationToken);

        var a = result.Urls.Single(u => u.Url.Contains("a.example"));
        a.ShippingCost.Should().Be(3m);
        a.LandedPrice.Should().Be(21m);
        var b = result.Urls.Single(u => u.Url.Contains("b.example"));
        b.ShippingCost.Should().Be(0m);
        b.LandedPrice.Should().Be(20m);
        result.LandedPrice.Should().Be(20m);
        result.LandedHost.Should().Be("b.example");
        result.ShippingCost.Should().Be(0m);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
