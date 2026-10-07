using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Comparisons;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Comparisons;

public class GetComparisonGroupHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly GetComparisonGroup.Handler _handler;
    private readonly Guid _testUserId;

    public GetComparisonGroupHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new GetComparisonGroup.Handler(_dbContext, TimeProvider.System, NullLogger<GetComparisonGroup.Handler>.Instance);
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
    public async Task Handle_WithValidGroup_ReturnsGroupDetails()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        _dbContext.ComparisonGroups.Add(group);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroup.Query(group.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Id.Should().Be(group.Id);
        result.Name.Should().Be("Headphones Comparison");
    }

    [Fact]
    public async Task Handle_WithNonExistentGroup_ThrowsNotFoundException()
    {
        // Arrange
        var query = new GetComparisonGroup.Query(Guid.NewGuid(), _testUserId);

        // Act
        var act = async () => await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Comparison group not found");
    }

    [Fact]
    public async Task Handle_WithGroupBelongingToDifferentUser_ThrowsNotFoundException()
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

        var otherGroup = new ComparisonGroup
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Other's Comparison"
        };
        _dbContext.ComparisonGroups.Add(otherGroup);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroup.Query(otherGroup.Id, _testUserId);

        // Act
        var act = async () => await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WithProducts_ReturnsProductsInGroup()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        var product1 = CreateProduct("Sony Headphones", 99.99m);
        var product2 = CreateProduct("Bose Headphones", 149.99m);
        product1.ComparisonGroupId = group.Id;
        product2.ComparisonGroupId = group.Id;

        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.AddRange(product1, product2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroup.Query(group.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Products.Should().HaveCount(2);
        result.Products.Should().Contain(p => p.Name == "Sony Headphones");
        result.Products.Should().Contain(p => p.Name == "Bose Headphones");
    }

    [Fact]
    public async Task Handle_WithMultipleProducts_IdentifiesBestPrice()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        var product1 = CreateProduct("Sony Headphones", 99.99m);
        var product2 = CreateProduct("Bose Headphones", 149.99m);
        var product3 = CreateProduct("Apple AirPods", 79.99m); // Cheapest
        product1.ComparisonGroupId = group.Id;
        product2.ComparisonGroupId = group.Id;
        product3.ComparisonGroupId = group.Id;

        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.AddRange(product1, product2, product3);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroup.Query(group.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.BestPriceProductId.Should().Be(product3.Id);
        result.BestPrice.Should().Be(79.99m);
        result.Products.Single(p => p.Id == product3.Id).IsBestPrice.Should().BeTrue();
        result.Products.Where(p => p.Id != product3.Id).Should().AllSatisfy(p => p.IsBestPrice.Should().BeFalse());
    }

    [Fact]
    public async Task Handle_WithSingleProduct_ThatProductIsBestPrice()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        var product = CreateProduct("Sony Headphones", 99.99m);
        product.ComparisonGroupId = group.Id;

        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroup.Query(group.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.BestPriceProductId.Should().Be(product.Id);
        result.BestPrice.Should().Be(99.99m);
        result.Products.Single().IsBestPrice.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithAllNullPrices_ReturnsNoBestPrice()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        var product1 = CreateProduct("Sony Headphones", null);
        var product2 = CreateProduct("Bose Headphones", null);
        product1.ComparisonGroupId = group.Id;
        product2.ComparisonGroupId = group.Id;

        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.AddRange(product1, product2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroup.Query(group.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.BestPriceProductId.Should().BeNull();
        result.BestPrice.Should().BeNull();
        result.Products.Should().AllSatisfy(p => p.IsBestPrice.Should().BeFalse());
    }

    [Fact]
    public async Task Handle_WithMixedNullAndNonNullPrices_ExcludesNullFromBestPrice()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        var product1 = CreateProduct("Sony Headphones", 99.99m);
        var product2 = CreateProduct("Bose Headphones", null); // Null price
        var product3 = CreateProduct("Apple AirPods", 149.99m);
        product1.ComparisonGroupId = group.Id;
        product2.ComparisonGroupId = group.Id;
        product3.ComparisonGroupId = group.Id;

        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.AddRange(product1, product2, product3);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroup.Query(group.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.BestPriceProductId.Should().Be(product1.Id); // Sony at 99.99 is cheapest non-null
        result.BestPrice.Should().Be(99.99m);
    }

    [Fact]
    public async Task Handle_WithMultipleProducts_ReturnsSortedByPriceAscending()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        var product1 = CreateProduct("Bose Headphones", 149.99m);
        var product2 = CreateProduct("Apple AirPods", 79.99m);
        var product3 = CreateProduct("Sony Headphones", 99.99m);
        product1.ComparisonGroupId = group.Id;
        product2.ComparisonGroupId = group.Id;
        product3.ComparisonGroupId = group.Id;

        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.AddRange(product1, product2, product3);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroup.Query(group.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert — products should be ordered by current price ascending
        result.Products.Should().HaveCount(3);
        result.Products[0].CurrentPrice.Should().Be(79.99m);
        result.Products[1].CurrentPrice.Should().Be(99.99m);
        result.Products[2].CurrentPrice.Should().Be(149.99m);
    }

    [Fact]
    public async Task Handle_WithNullPrices_SortsNullPricesLast()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        var product1 = CreateProduct("Pending Product", null);
        var product2 = CreateProduct("Cheap Product", 49.99m);
        var product3 = CreateProduct("Expensive Product", 199.99m);
        product1.ComparisonGroupId = group.Id;
        product2.ComparisonGroupId = group.Id;
        product3.ComparisonGroupId = group.Id;

        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.AddRange(product1, product2, product3);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroup.Query(group.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert — priced products first (ascending), null prices last
        result.Products.Should().HaveCount(3);
        result.Products[0].CurrentPrice.Should().Be(49.99m);
        result.Products[1].CurrentPrice.Should().Be(199.99m);
        result.Products[2].CurrentPrice.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithNoProducts_ReturnsEmptyProductList()
    {
        // Arrange
        var group = CreateGroup("Empty Comparison");
        _dbContext.ComparisonGroups.Add(group);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroup.Query(group.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Products.Should().BeEmpty();
        result.BestPriceProductId.Should().BeNull();
        result.BestPrice.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithPriceHistory_IncludesHistoryInResponse()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        var product = CreateProduct("Sony Headphones", 99.99m);
        product.ComparisonGroupId = group.Id;

        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Add price history
        var pricePoints = new[]
        {
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 109.99m, RecordedAt = DateTime.UtcNow.AddDays(-5) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, Price = 99.99m, RecordedAt = DateTime.UtcNow.AddDays(-2) }
        };
        _dbContext.PricePoints.AddRange(pricePoints);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroup.Query(group.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        var productResult = result.Products.Single();
        productResult.PriceHistory.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_ReturnsCorrectProductDetails()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        var product = CreateProduct("Sony Headphones", 99.99m);
        product.ComparisonGroupId = group.Id;
        product.ImageUrl = "https://example.com/sony.jpg";
        product.Currency = "USD";

        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetComparisonGroup.Query(group.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        var productResult = result.Products.Single();
        productResult.Id.Should().Be(product.Id);
        productResult.Name.Should().Be("Sony Headphones");
        productResult.Url.Should().NotBeNullOrEmpty();
        productResult.ImageUrl.Should().Be("https://example.com/sony.jpg");
        productResult.CurrentPrice.Should().Be(99.99m);
        productResult.Currency.Should().Be("USD");
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

    private Product CreateProduct(string name, decimal? price)
    {
        var product = TestEntityFactory.Product(_testUserId).Named(name).Priced(price).Build();
        // Seed a ProductUrl so the handler can resolve the Url field
        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = $"https://example.com/product/{Guid.NewGuid()}",
            Currency = "USD"
        });
        return product;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
