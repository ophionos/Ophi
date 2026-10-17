using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Api.Features.Comparisons;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Microsoft.Extensions.Time.Testing;

namespace Ophi.Api.Tests.Unit.Comparisons;

public class GetComparisonGroupsHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly GetComparisonGroups.Handler _handler;
    private readonly Guid _testUserId;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));

    public GetComparisonGroupsHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new GetComparisonGroups.Handler(_dbContext, _time, NullLogger<GetComparisonGroups.Handler>.Instance);
        _testUserId = Guid.NewGuid();

        // Seed test user for FK constraints
        var testUser = new User
        {
            Id = _testUserId,
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hash"
        };
        _dbContext.Users.Add(testUser);
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task Handle_WithNoGroups_ReturnsEmptyList()
    {
        // Arrange
        var query = new GetComparisonGroups.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithGroups_ReturnsUserGroups()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        _dbContext.ComparisonGroups.Add(group);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroups.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Id.Should().Be(group.Id);
    }

    [Fact]
    public async Task Handle_ReturnsCorrectGroupData()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        _dbContext.ComparisonGroups.Add(group);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroups.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        var groupDto = result.Items[0];
        groupDto.Name.Should().Be("Headphones Comparison");
        groupDto.ProductCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_OnlyReturnsGroupsForRequestedUser()
    {
        // Arrange
        var otherUserId = Guid.NewGuid();
        var otherUser = new User
        {
            Id = otherUserId,
            Email = "other@example.com",
            Name = "Other User",
            PasswordHash = "hash"
        };
        _dbContext.Users.Add(otherUser);

        var myGroup = CreateGroup("My Headphones");

        var otherGroup = new ComparisonGroup
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Other's Comparison"
        };

        _dbContext.ComparisonGroups.AddRange(myGroup, otherGroup);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroups.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Id.Should().Be(myGroup.Id);
    }

    [Fact]
    public async Task Handle_ReturnsGroupsOrderedByName()
    {
        // Arrange
        var groupZ = CreateGroup("Zebra Comparison");
        var groupA = CreateGroup("Apple Comparison");
        var groupM = CreateGroup("Mango Comparison");

        _dbContext.ComparisonGroups.AddRange(groupZ, groupA, groupM);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroups.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(3);
        result.Items[0].Name.Should().Be("Apple Comparison");
        result.Items[1].Name.Should().Be("Mango Comparison");
        result.Items[2].Name.Should().Be("Zebra Comparison");
    }

    [Fact]
    public async Task Handle_WithGroupContainingProducts_ReturnsCorrectProductCount()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        _dbContext.ComparisonGroups.Add(group);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Add products to the group
        var products = new[]
        {
            CreateProduct("Product 1", group.Id),
            CreateProduct("Product 2", group.Id),
            CreateProduct("Product 3", group.Id)
        };
        _dbContext.Products.AddRange(products);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroups.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].ProductCount.Should().Be(3);
    }

    [Fact]
    public async Task Handle_WithMultipleGroups_ReturnsCorrectProductCountsForEach()
    {
        // Arrange
        var group1 = CreateGroup("Group 1");
        var group2 = CreateGroup("Group 2");
        _dbContext.ComparisonGroups.AddRange(group1, group2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Add products to group 1
        var productsForGroup1 = new[]
        {
            CreateProduct("Product 1A", group1.Id),
            CreateProduct("Product 1B", group1.Id)
        };
        _dbContext.Products.AddRange(productsForGroup1);

        // Add product to group 2
        var productForGroup2 = CreateProduct("Product 2A", group2.Id);
        _dbContext.Products.Add(productForGroup2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroups.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items.First(g => g.Name == "Group 1").ProductCount.Should().Be(2);
        result.Items.First(g => g.Name == "Group 2").ProductCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithMixedCurrencies_ReturnsTheBestPriceOfTheDominantCurrency()
    {
        // The card shows the same best price as the detail page: the currency first, then the MIN.
        var group = CreateGroup("Mixed");
        _dbContext.ComparisonGroups.Add(group);
        var usdCheap = CreatePricedProduct("US cheap", group.Id, 100m);
        _dbContext.Products.AddRange(
            CreatePricedProduct("EU listing", group.Id, 80m, "EUR"),
            usdCheap,
            CreatePricedProduct("US dear", group.Id, 120m),
            CreateProduct("Unpriced", group.Id));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _handler.Handle(new GetComparisonGroups.Query(_testUserId), TestContext.Current.CancellationToken);

        var dto = result.Items.Single();
        dto.ProductCount.Should().Be(4);
        dto.BestPriceProductId.Should().Be(usdCheap.Id);
        dto.BestPriceProductName.Should().Be("US cheap");
        dto.BestPrice.Should().Be(100m);
        dto.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Handle_WithNoPricedProducts_ReturnsNoBestPriceAndNoSparkline()
    {
        var group = CreateGroup("Unpriced");
        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.Add(CreateProduct("Unpriced", group.Id));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _handler.Handle(new GetComparisonGroups.Query(_testUserId), TestContext.Current.CancellationToken);

        var dto = result.Items.Single();
        dto.BestPriceProductId.Should().BeNull();
        dto.BestPrice.Should().BeNull();
        dto.Currency.Should().BeNull();
        dto.Sparkline.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsTheBestProductsDailyLowsInItsCurrencyInDateOrder()
    {
        var group = CreateGroup("History");
        _dbContext.ComparisonGroups.Add(group);
        var best = CreatePricedProduct("Best", group.Id, 40m);
        var other = CreatePricedProduct("Other", group.Id, 60m);
        _dbContext.Products.AddRange(best, other);
        var now = _time.GetUtcNow().UtcDateTime;
        _dbContext.PricePoints.AddRange(
            Point(best.Id, 50m, now.AddDays(-2)),
            Point(best.Id, 45m, now.AddDays(-5)),
            Point(best.Id, 48m, now.AddDays(-5).AddHours(-1)),
            Point(best.Id, 30m, now.AddDays(-3), "EUR"),
            Point(best.Id, 70m, now.AddDays(-40)),
            Point(other.Id, 10m, now.AddDays(-1)));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _handler.Handle(new GetComparisonGroups.Query(_testUserId), TestContext.Current.CancellationToken);

        result.Items.Single().Sparkline.Select(s => (s.Date, s.Price)).Should().Equal(
            ("2026-10-03", 45m),
            ("2026-10-06", 50m));
    }

    [Fact]
    public async Task Handle_WithEqualBestPrices_NamesTheSameProductAsTheDetailPage()
    {
        var group = CreateGroup("Tie");
        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.AddRange(
            CreatePricedProduct("Tie A", group.Id, 30m),
            CreatePricedProduct("Tie B", group.Id, 30m),
            CreatePricedProduct("Tie C", group.Id, 30m));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var list = await _handler.Handle(new GetComparisonGroups.Query(_testUserId), TestContext.Current.CancellationToken);
        var detail = await new GetComparisonGroup.Handler(_dbContext, _time, NullLogger<GetComparisonGroup.Handler>.Instance)
            .Handle(new GetComparisonGroup.Query(group.Id, _testUserId), TestContext.Current.CancellationToken);

        list.Items.Single().BestPriceProductId.Should().Be(detail.BestPriceProductId);
    }

    private ComparisonGroup CreateGroup(string name)
    {
        return new ComparisonGroup
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = name
        };
    }

    private Product CreateProduct(string name, Guid comparisonGroupId) =>
        TestEntityFactory.Product(_testUserId).Named(name).InGroup(comparisonGroupId).Build();

    private Product CreatePricedProduct(string name, Guid comparisonGroupId, decimal price, string currency = "USD") =>
        TestEntityFactory.Product(_testUserId).Named(name).InGroup(comparisonGroupId).Priced(price).WithCurrency(currency).Build();

    private static PricePoint Point(Guid productId, decimal price, DateTime recordedAt, string currency = "USD") =>
        new() { Id = Guid.NewGuid(), ProductId = productId, Price = price, Currency = currency, RecordedAt = recordedAt };

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
