using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Features.Products;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Products;

public class GetProductsHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly GetProducts.Handler _handler;
    private readonly Guid _testUserId;

    public GetProductsHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new GetProducts.Handler(_dbContext, TimeProvider.System, NullLogger<GetProducts.Handler>.Instance);
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
    public async Task Handle_WithNoProducts_ReturnsEmptyList()
    {
        // Arrange
        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithProducts_ReturnsUserProducts()
    {
        // Arrange
        var product1 = CreateProduct("Product 1", "https://example.com/1");
        var product2 = CreateProduct("Product 2", "https://example.com/2");
        _dbContext.Products.AddRange(product1, product2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(2);
        result.Total.Should().Be(2);
    }

    [Fact]
    public async Task Handle_WithProducts_ReturnsCorrectProductData()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        product.ImageUrl = "https://example.com/image.jpg";
        product.CurrentPrice = 99.99m;
        product.PreviousPrice = 120.00m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
        var dto = result.Items[0];
        dto.Id.Should().Be(product.Id);
        dto.Name.Should().Be("Test Product");
        dto.Url.Should().Be("https://example.com/product");
        dto.ImageUrl.Should().Be("https://example.com/image.jpg");
        dto.CurrentPrice.Should().Be(99.99m);
        dto.PreviousPrice.Should().Be(120.00m);
        dto.Currency.Should().Be("USD");
        dto.Status.Should().Be("active");
    }

    [Fact]
    public async Task Handle_WithPriceChange_CalculatesPriceChangePercentage()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        product.CurrentPrice = 80.00m;
        product.PreviousPrice = 100.00m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        var dto = result.Items[0];
        dto.PriceChange.Should().Be(-20.00m); // (80 - 100) / 100 * 100 = -20%
    }

    [Fact]
    public async Task Handle_WithPriceIncrease_CalculatesPositivePriceChange()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        product.CurrentPrice = 150.00m;
        product.PreviousPrice = 100.00m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        var dto = result.Items[0];
        dto.PriceChange.Should().Be(50.00m); // (150 - 100) / 100 * 100 = 50%
    }

    [Fact]
    public async Task Handle_WithNoPreviousPrice_ReturnsNullPriceChange()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        product.CurrentPrice = 99.99m;
        product.PreviousPrice = null;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        var dto = result.Items[0];
        dto.PriceChange.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithZeroPreviousPrice_ReturnsNullPriceChange()
    {
        // Arrange
        var product = CreateProduct("Test Product", "https://example.com/product");
        product.CurrentPrice = 99.99m;
        product.PreviousPrice = 0m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        var dto = result.Items[0];
        dto.PriceChange.Should().BeNull();
    }

    [Fact]
    public async Task Handle_OnlyReturnsProductsForRequestedUser()
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

        var myProduct = CreateProduct("My Product", "https://example.com/mine");
        var otherProduct = CreateProduct("Other Product", "https://example.com/other");
        otherProduct.UserId = otherUserId;

        _dbContext.Products.AddRange(myProduct, otherProduct);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("My Product");
    }

    [Fact]
    public async Task Handle_ReturnsProductsOrderedByUpdatedAtDescending()
    {
        // Arrange
        var oldProduct = CreateProduct("Old Product", "https://example.com/old");
        var newProduct = CreateProduct("New Product", "https://example.com/new");
        var middleProduct = CreateProduct("Middle Product", "https://example.com/middle");

        _dbContext.Products.AddRange(oldProduct, newProduct, middleProduct);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Update timestamps directly in database to bypass SaveChangesAsync auto-update
        var baseTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE Products SET UpdatedAt = {0} WHERE Id = {1}", baseTime.AddDays(-5), oldProduct.Id);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE Products SET UpdatedAt = {0} WHERE Id = {1}", baseTime, newProduct.Id);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE Products SET UpdatedAt = {0} WHERE Id = {1}", baseTime.AddDays(-2), middleProduct.Id);

        _dbContext.ChangeTracker.Clear();

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(3);
        result.Items[0].Name.Should().Be("New Product");
        result.Items[1].Name.Should().Be("Middle Product");
        result.Items[2].Name.Should().Be("Old Product");
    }

    // Search filter tests
    [Fact]
    public async Task Handle_WithSearchTerm_FiltersProductsByName()
    {
        // Arrange
        var product1 = CreateProduct("Sony Headphones", "https://example.com/1");
        var product2 = CreateProduct("Samsung TV", "https://example.com/2");
        _dbContext.Products.AddRange(product1, product2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Search: "headphones");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("Sony Headphones");
    }

    [Fact]
    public async Task Handle_WithSearchTerm_FiltersProductsByUrl()
    {
        // Arrange
        var product1 = CreateProduct("Product A", "https://amazon.com/headphones");
        var product2 = CreateProduct("Product B", "https://ebay.com/tv");
        _dbContext.Products.AddRange(product1, product2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Search: "amazon");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("Product A");
    }

    [Fact]
    public async Task Handle_WithSearchTerm_IsCaseInsensitive()
    {
        // Arrange
        var product = CreateProduct("Sony Headphones", "https://example.com/1");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Search: "SONY");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("Sony Headphones");
    }

    [Fact]
    public async Task Handle_WithSearchTerm_MatchesPartialName()
    {
        // Arrange
        var product = CreateProduct("Sony WH-1000XM5 Headphones", "https://example.com/1");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Search: "1000XM5");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WithEmptySearch_ReturnsAllProducts()
    {
        // Arrange
        var product1 = CreateProduct("Product 1", "https://example.com/1");
        var product2 = CreateProduct("Product 2", "https://example.com/2");
        _dbContext.Products.AddRange(product1, product2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Search: "  ");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithSearchTermContainingPercent_MatchesOnlyLiteralPercent()
    {
        // Arrange — "%" is a LIKE wildcard; without escaping, "50%" matches any name containing "50".
        var literal = CreateProduct("50% off bundle", "https://example.com/1");
        var decoy = CreateProduct("500 GB SSD", "https://example.com/2");
        _dbContext.Products.AddRange(literal, decoy);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Search: "50%");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert — only the product whose name literally contains "50%" matches.
        result.Items.Should().ContainSingle();
        result.Items[0].Name.Should().Be("50% off bundle");
    }

    [Fact]
    public async Task Handle_WithSearchTermContainingUnderscore_MatchesOnlyLiteralUnderscore()
    {
        // Arrange — "_" is a single-char LIKE wildcard; "a_b" would otherwise match "axb".
        var literal = CreateProduct("model a_b cable", "https://example.com/1");
        var decoy = CreateProduct("model axb cable", "https://example.com/2");
        _dbContext.Products.AddRange(literal, decoy);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Search: "a_b");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().ContainSingle();
        result.Items[0].Name.Should().Be("model a_b cable");
    }

    // Status filter tests
    [Theory]
    [InlineData("active", ProductStatus.Error, "Active Product")]
    [InlineData("paused", ProductStatus.Active, "Paused Product")]
    public async Task Handle_WithStatusFilter_ReturnsOnlyMatchingStatus(
        string filterStatus, ProductStatus otherStatus, string expectedName)
    {
        // Arrange — the "match" product's status is whatever the filter is asking for;
        // the other product gets a non-matching status.
        var matchStatus = filterStatus switch
        {
            "active" => ProductStatus.Active,
            "paused" => ProductStatus.Paused,
            _ => throw new ArgumentException($"unhandled filter: {filterStatus}")
        };
        var matchProduct = CreateProduct(expectedName, "https://example.com/1", status: matchStatus);
        var otherProduct = CreateProduct("Other Product", "https://example.com/2", status: otherStatus);
        _dbContext.Products.AddRange(matchProduct, otherProduct);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Status: filterStatus);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().ContainSingle().Which.Name.Should().Be(expectedName);
    }

    [Fact]
    public async Task Handle_WithInvalidStatusFilter_ReturnsAllProducts()
    {
        // Arrange
        var product1 = CreateProduct("Product 1", "https://example.com/1");
        var product2 = CreateProduct("Product 2", "https://example.com/2");
        _dbContext.Products.AddRange(product1, product2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Status: "invalidstatus");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2);
    }

    // Sort tests
    [Theory]
    [InlineData("asc", new[] { "Apple", "Banana", "Cherry" })]
    [InlineData("desc", new[] { "Cherry", "Banana", "Apple" })]
    public async Task Handle_WithSortByName_SortsInDirection(string direction, string[] expectedOrder)
    {
        // Arrange — names added out of order so sort is observable
        _dbContext.Products.AddRange(
            CreateProduct("Cherry", "https://example.com/3"),
            CreateProduct("Apple", "https://example.com/1"),
            CreateProduct("Banana", "https://example.com/2"));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, SortBy: "name", SortDirection: direction);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Select(i => i.Name).Should().Equal(expectedOrder);
    }

    [Fact]
    public async Task Handle_WithSortByPrice_SortsByCurrentPrice()
    {
        // Arrange
        var expensive = CreateProduct("Expensive", "https://example.com/1");
        expensive.CurrentPrice = 200m;
        var cheap = CreateProduct("Cheap", "https://example.com/2");
        cheap.CurrentPrice = 50m;
        var mid = CreateProduct("Mid", "https://example.com/3");
        mid.CurrentPrice = 100m;
        _dbContext.Products.AddRange(expensive, cheap, mid);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, SortBy: "price", SortDirection: "asc");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].Name.Should().Be("Cheap");
        result.Items[1].Name.Should().Be("Mid");
        result.Items[2].Name.Should().Be("Expensive");
    }

    [Fact]
    public async Task Handle_WithSortByDateAdded_SortsByCreatedAt()
    {
        // Arrange
        var product1 = CreateProduct("First", "https://example.com/1");
        var product2 = CreateProduct("Second", "https://example.com/2");
        var product3 = CreateProduct("Third", "https://example.com/3");
        _dbContext.Products.AddRange(product1, product2, product3);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Set CreatedAt via raw SQL to bypass auto-update
        var baseTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE Products SET CreatedAt = {0} WHERE Id = {1}", baseTime.AddDays(-10), product1.Id);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE Products SET CreatedAt = {0} WHERE Id = {1}", baseTime, product2.Id);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE Products SET CreatedAt = {0} WHERE Id = {1}", baseTime.AddDays(-5), product3.Id);
        _dbContext.ChangeTracker.Clear();

        var query = new GetProducts.Query(_testUserId, SortBy: "dateadded", SortDirection: "asc");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].Name.Should().Be("First");
        result.Items[1].Name.Should().Be("Third");
        result.Items[2].Name.Should().Be("Second");
    }

    [Fact]
    public async Task Handle_WithSortByPriceChange_SortsByCalculatedPriceChange()
    {
        // Arrange
        var bigDrop = CreateProduct("Big Drop", "https://example.com/1");
        bigDrop.CurrentPrice = 50m;
        bigDrop.PreviousPrice = 100m; // -50%

        var smallDrop = CreateProduct("Small Drop", "https://example.com/2");
        smallDrop.CurrentPrice = 90m;
        smallDrop.PreviousPrice = 100m; // -10%

        var increase = CreateProduct("Increase", "https://example.com/3");
        increase.CurrentPrice = 150m;
        increase.PreviousPrice = 100m; // +50%

        _dbContext.Products.AddRange(bigDrop, smallDrop, increase);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, SortBy: "pricechange", SortDirection: "asc");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].Name.Should().Be("Big Drop");     // -50%
        result.Items[1].Name.Should().Be("Small Drop");   // -10%
        result.Items[2].Name.Should().Be("Increase");     // +50%
    }

    [Fact]
    public async Task Handle_WithNoSortBy_DefaultsToUpdatedAtDescending()
    {
        // Arrange
        var oldProduct = CreateProduct("Old Product", "https://example.com/old");
        var newProduct = CreateProduct("New Product", "https://example.com/new");
        _dbContext.Products.AddRange(oldProduct, newProduct);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var baseTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE Products SET UpdatedAt = {0} WHERE Id = {1}", baseTime.AddDays(-5), oldProduct.Id);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE Products SET UpdatedAt = {0} WHERE Id = {1}", baseTime, newProduct.Id);
        _dbContext.ChangeTracker.Clear();

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].Name.Should().Be("New Product");
        result.Items[1].Name.Should().Be("Old Product");
    }

    // Combined filter tests
    [Fact]
    public async Task Handle_WithSearchAndStatusFilter_AppliesBothFilters()
    {
        // Arrange
        var activeMatch = CreateProduct("Sony Headphones", "https://example.com/1", status: ProductStatus.Active);
        var pausedMatch = CreateProduct("Sony TV", "https://example.com/2", status: ProductStatus.Paused);
        var activeNoMatch = CreateProduct("Samsung TV", "https://example.com/3", status: ProductStatus.Active);
        _dbContext.Products.AddRange(activeMatch, pausedMatch, activeNoMatch);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Search: "sony", Status: "active");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("Sony Headphones");
    }

    [Fact]
    public async Task Handle_WithSearchAndSort_AppliesBothSearchAndSort()
    {
        // Arrange
        var product1 = CreateProduct("Sony A", "https://example.com/1");
        product1.CurrentPrice = 200m;
        var product2 = CreateProduct("Sony B", "https://example.com/2");
        product2.CurrentPrice = 50m;
        var product3 = CreateProduct("Samsung C", "https://example.com/3");
        product3.CurrentPrice = 10m;
        _dbContext.Products.AddRange(product1, product2, product3);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Search: "sony", SortBy: "price", SortDirection: "asc");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items[0].Name.Should().Be("Sony B");
        result.Items[1].Name.Should().Be("Sony A");
    }

    [Fact]
    public async Task Handle_WithDifferentStatuses_ReturnsCorrectStatusString()
    {
        // Arrange
        var activeProduct = CreateProduct("Active", "https://example.com/active", status: ProductStatus.Active);

        var errorProduct = CreateProduct("Error", "https://example.com/error", status: ProductStatus.Error);

        var pausedProduct = CreateProduct("Paused", "https://example.com/paused", status: ProductStatus.Paused);

        _dbContext.Products.AddRange(activeProduct, errorProduct, pausedProduct);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().Contain(p => p.Status == "active");
        result.Items.Should().Contain(p => p.Status == "error");
        result.Items.Should().Contain(p => p.Status == "paused");
    }

    // IsFavourite tests
    [Fact]
    public async Task Handle_WithFavouriteProducts_ReturnsIsFavouriteField()
    {
        // Arrange
        var favouriteProduct = CreateProduct("Favourite", "https://example.com/fav");
        favouriteProduct.IsFavourite = true;
        var regularProduct = CreateProduct("Regular", "https://example.com/regular");
        regularProduct.IsFavourite = false;
        _dbContext.Products.AddRange(favouriteProduct, regularProduct);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().Contain(p => p.IsFavourite == true && p.Name == "Favourite");
        result.Items.Should().Contain(p => p.IsFavourite == false && p.Name == "Regular");
    }

    [Fact]
    public async Task Handle_DefaultsToFavouritesFirst_WhenNoSortSpecified()
    {
        // Arrange
        var regular1 = CreateProduct("Regular A", "https://example.com/1");
        regular1.IsFavourite = false;
        var favourite = CreateProduct("Favourite B", "https://example.com/2");
        favourite.IsFavourite = true;
        var regular2 = CreateProduct("Regular C", "https://example.com/3");
        regular2.IsFavourite = false;
        _dbContext.Products.AddRange(regular1, favourite, regular2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Set UpdatedAt to ensure consistent secondary sort
        var baseTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE Products SET UpdatedAt = {0} WHERE Id = {1}", baseTime, regular1.Id);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE Products SET UpdatedAt = {0} WHERE Id = {1}", baseTime.AddHours(-1), favourite.Id);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE Products SET UpdatedAt = {0} WHERE Id = {1}", baseTime.AddHours(-2), regular2.Id);
        _dbContext.ChangeTracker.Clear();

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert - favourite should be first despite older UpdatedAt
        result.Items[0].Name.Should().Be("Favourite B");
    }

    [Fact]
    public async Task Handle_WithSortByName_FavouritesStillFirst()
    {
        // Arrange
        var regularA = CreateProduct("A Product", "https://example.com/1");
        regularA.IsFavourite = false;
        var favouriteZ = CreateProduct("Z Product", "https://example.com/2");
        favouriteZ.IsFavourite = true;
        _dbContext.Products.AddRange(regularA, favouriteZ);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, SortBy: "name", SortDirection: "asc");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert - favourite Z should come before regular A due to favourite priority
        result.Items[0].Name.Should().Be("Z Product");
        result.Items[1].Name.Should().Be("A Product");
    }

    // Pagination tests
    [Fact]
    public async Task Handle_WithDefaultPagination_ReturnsFirstPageOf24()
    {
        // Arrange
        for (var i = 0; i < 30; i++)
        {
            _dbContext.Products.Add(CreateProduct($"Product {i}", $"https://example.com/{i}"));
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(24);
        result.Total.Should().Be(30);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(24);
    }

    [Fact]
    public async Task Handle_WithCustomPageAndPageSize_ReturnsCorrectPage()
    {
        // Arrange
        for (var i = 0; i < 10; i++)
        {
            var product = CreateProduct($"Product {i:D2}", $"https://example.com/{i}");
            product.CurrentPrice = i;
            _dbContext.Products.Add(product);
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Page: 2, PageSize: 3);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(3);
        result.Total.Should().Be(10);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(3);
    }

    [Fact]
    public async Task Handle_WithPageBeyondResults_ReturnsEmptyItemsWithCorrectTotal()
    {
        // Arrange
        for (var i = 0; i < 5; i++)
        {
            _dbContext.Products.Add(CreateProduct($"Product {i}", $"https://example.com/{i}"));
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Page: 100, PageSize: 10);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().BeEmpty();
        result.Total.Should().Be(5);
        result.Page.Should().Be(100);
    }

    [Fact]
    public async Task Handle_WithPageSizeExceeding100_CapsAt100()
    {
        // Arrange
        _dbContext.Products.Add(CreateProduct("Product 1", "https://example.com/1"));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, PageSize: 200);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task Handle_WithFiltersAndPagination_TotalReflectsFilteredCount()
    {
        // Arrange
        for (var i = 0; i < 5; i++)
        {
            var product = CreateProduct($"Sony Product {i}", $"https://example.com/sony{i}");
            _dbContext.Products.Add(product);
        }
        for (var i = 0; i < 3; i++)
        {
            var product = CreateProduct($"Samsung Product {i}", $"https://example.com/samsung{i}");
            _dbContext.Products.Add(product);
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Search: "Sony", Page: 1, PageSize: 2);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Total.Should().Be(5);
    }

    // Pagination edge case tests
    [Theory]
    [InlineData(0)]   // page=0 clamps to 1
    [InlineData(-5)]  // negative page clamps to 1
    public async Task Handle_WithInvalidPage_ClampsToPageOne(int page)
    {
        _dbContext.Products.Add(CreateProduct("Product 1", "https://example.com/1"));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Page: page);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Page.Should().Be(1);
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WithPageSizeZero_ClampsToPageSizeOne()
    {
        _dbContext.Products.Add(CreateProduct("Product 1", "https://example.com/1"));
        _dbContext.Products.Add(CreateProduct("Product 2", "https://example.com/2"));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, PageSize: 0);

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.PageSize.Should().Be(1);
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WithLastPartialPage_ReturnsRemainingItems()
    {
        // Arrange — 5 items, pageSize=3, page=2 should return 2 items
        for (var i = 0; i < 5; i++)
        {
            _dbContext.Products.Add(CreateProduct($"Product {i}", $"https://example.com/{i}"));
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Page: 2, PageSize: 3);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Total.Should().Be(5);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(3);
    }

    [Fact]
    public async Task Handle_WithSearchAndPagination_TotalReflectsFilteredResultCount()
    {
        // Arrange — 4 Sony products, 3 Samsung; page 1 of 2 with pageSize=2 for Sony search
        for (var i = 0; i < 4; i++)
        {
            _dbContext.Products.Add(CreateProduct($"Sony Product {i}", $"https://example.com/sony{i}"));
        }
        for (var i = 0; i < 3; i++)
        {
            _dbContext.Products.Add(CreateProduct($"Samsung Product {i}", $"https://example.com/samsung{i}"));
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Search: "Sony", Page: 2, PageSize: 2);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert — total is filtered count (4 Sony), page 2 returns remaining 2
        result.Total.Should().Be(4);
        result.Items.Should().HaveCount(2);
        result.Page.Should().Be(2);
    }

    // --- Sparkline / PriceMin / PriceMax Tests ---

    [Fact]
    public async Task Handle_WithIncludeSparkline_ReturnsSparklineData()
    {
        // Arrange — 7 price points over 7 days
        var product = CreateProduct("Sparkline Product", "https://example.com/sp1");
        _dbContext.Products.Add(product);
        for (var i = 0; i < 7; i++)
            _dbContext.PricePoints.Add(CreatePricePoint(product, 100m - i, daysAgo: i));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, IncludeSparkline: true);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Sparkline.Should().NotBeNull();
        result.Items[0].Sparkline.Should().HaveCount(7);
    }

    [Fact]
    public async Task Handle_WithoutIncludeSparkline_ReturnsNullSparkline()
    {
        // Arrange
        var product = CreateProduct("No Sparkline", "https://example.com/nosp");
        _dbContext.Products.Add(product);
        _dbContext.PricePoints.Add(CreatePricePoint(product, 50m));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].Sparkline.Should().BeNull();
        result.Items[0].PriceMin.Should().BeNull();
        result.Items[0].PriceMax.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithIncludeSparkline_ReturnsMinMax()
    {
        // Arrange
        var product = CreateProduct("MinMax Product", "https://example.com/mm");
        _dbContext.Products.Add(product);
        _dbContext.PricePoints.AddRange(
            CreatePricePoint(product, 50m, daysAgo: 3),
            CreatePricePoint(product, 120m, daysAgo: 2),
            CreatePricePoint(product, 80m, daysAgo: 1));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, IncludeSparkline: true);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].PriceMin.Should().Be(50m);
        result.Items[0].PriceMax.Should().Be(120m);
    }

    [Fact]
    public async Task Handle_WithIncludeSparkline_NoHistory_ReturnsEmptySparkline()
    {
        // Arrange
        var product = CreateProduct("No History", "https://example.com/nh");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, IncludeSparkline: true);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].Sparkline.Should().NotBeNull();
        result.Items[0].Sparkline.Should().BeEmpty();
        result.Items[0].PriceMin.Should().BeNull();
        result.Items[0].PriceMax.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithIncludeSparkline_MultipleProducts_BatchesCorrectly()
    {
        // Arrange
        var product1 = CreateProduct("Product A", "https://example.com/a");
        var product2 = CreateProduct("Product B", "https://example.com/b");
        _dbContext.Products.AddRange(product1, product2);

        // Product 1: prices 10, 20, 30 over 3 days
        _dbContext.PricePoints.AddRange(
            CreatePricePoint(product1, 10m, daysAgo: 3),
            CreatePricePoint(product1, 20m, daysAgo: 2),
            CreatePricePoint(product1, 30m, daysAgo: 1));
        // Product 2: prices 100, 200 over 2 days
        _dbContext.PricePoints.AddRange(
            CreatePricePoint(product2, 100m, daysAgo: 2),
            CreatePricePoint(product2, 200m, daysAgo: 1));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, IncludeSparkline: true);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        var itemA = result.Items.First(i => i.Name == "Product A");
        var itemB = result.Items.First(i => i.Name == "Product B");

        itemA.Sparkline.Should().HaveCount(3);
        itemA.PriceMin.Should().Be(10m);
        itemA.PriceMax.Should().Be(30m);

        itemB.Sparkline.Should().HaveCount(2);
        itemB.PriceMin.Should().Be(100m);
        itemB.PriceMax.Should().Be(200m);
    }

    // --- Deal Score Tests ---

    [Theory]
    [InlineData(50, true, 70, 100)]   // at min + declining → high score (>=70)
    [InlineData(200, false, 0, 30)]   // at max + rising → low score (<=30)
    public async Task Handle_WithIncludeSparkline_ComputesDealScore(
        int currentPrice,
        bool declining,
        int minExpected,
        int maxExpected)
    {
        // Arrange — declining=true ⇒ 100→50, declining=false ⇒ 100→200
        var product = CreateProduct("Deal Product", "https://example.com/deal", currentPrice: currentPrice);
        _dbContext.Products.Add(product);
        for (var i = 0; i < 6; i++)
        {
            var price = declining ? 100m - (i * 10) : 100m + (i * 20);
            _dbContext.PricePoints.Add(CreatePricePoint(product, price, daysAgo: 5 - i));
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, IncludeSparkline: true);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].DealScore.Should().NotBeNull();
        result.Items[0].DealScore.Should().BeGreaterThanOrEqualTo(minExpected);
        result.Items[0].DealScore.Should().BeLessThanOrEqualTo(maxExpected);
    }

    [Fact]
    public async Task Handle_WithNoHistory_ReturnsNullDealScore()
    {
        // Arrange — product with no price points
        var product = CreateProduct("No History", "https://example.com/nohist", currentPrice: 100m);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, IncludeSparkline: true);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].DealScore.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithoutSparklineFlag_DoesNotComputeDealScore()
    {
        // Arrange
        var product = CreateProduct("No Flag", "https://example.com/noflag");
        product.CurrentPrice = 50m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var urlId = product.ProductUrls.First().Id;
        _dbContext.PricePoints.Add(new PricePoint
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ProductUrlId = urlId,
            Price = 50m,
            Currency = "USD",
            RecordedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].DealScore.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithAlertCloseToTriggering_BoostsDealScore()
    {
        // Arrange — product with alert just above current price
        var product = CreateProduct("Alert Product", "https://example.com/alert");
        product.CurrentPrice = 55m;
        _dbContext.Products.Add(product);

        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            UserId = _testUserId,
            TargetPrice = 50m,
            ReferencePrice = 100m,
            Condition = AlertCondition.Below,
            IsActive = true
        };
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var urlId = product.ProductUrls.First().Id;
        // Flat prices around 55
        for (var i = 0; i < 5; i++)
        {
            _dbContext.PricePoints.Add(new PricePoint
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                ProductUrlId = urlId,
                Price = 55m,
                Currency = "USD",
                RecordedAt = DateTime.UtcNow.AddDays(-4 + i)
            });
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, IncludeSparkline: true);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert — close to alert target should boost score
        result.Items[0].DealScore.Should().NotBeNull();
        result.Items[0].DealScore.Should().BeGreaterThanOrEqualTo(50);
    }

    [Fact]
    public async Task Handle_WithNullCurrentPrice_ReturnsNullDealScore()
    {
        // Arrange
        var product = CreateProduct("No Price", "https://example.com/noprice");
        product.CurrentPrice = null;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var urlId = product.ProductUrls.First().Id;
        _dbContext.PricePoints.Add(new PricePoint
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ProductUrlId = urlId,
            Price = 50m,
            Currency = "USD",
            RecordedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, IncludeSparkline: true);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].DealScore.Should().BeNull();
    }

    // --- Affiliate URL Tests ---

    [Fact]
    public async Task Handle_WithAffiliatesEnabled_ReturnsAffiliateUrl()
    {
        // Arrange
        var product = CreateProduct("Affiliate Product", "https://example.com/product", storeId: "my-store");
        _dbContext.Products.Add(product);

        var user = await _dbContext.Users.FindAsync([_testUserId], TestContext.Current.CancellationToken);
        user!.AffiliatesEnabled = true;

        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            StoreId = "my-store",
            Name = "My Store",
            DomainPatternsJson = "[]",
            SelectorsJson = "{}",
            AffiliateParamName = "ref",
            AffiliateTag = "ophi-20"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].AffiliateUrl.Should().Contain("ref=ophi-20");
    }

    [Fact]
    public async Task Handle_WithAffiliatesDisabled_ReturnsNullAffiliateUrl()
    {
        // Arrange
        var product = CreateProduct("Product", "https://example.com/product", storeId: "my-store");
        _dbContext.Products.Add(product);

        var user = await _dbContext.Users.FindAsync([_testUserId], TestContext.Current.CancellationToken);
        user!.AffiliatesEnabled = false;

        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            StoreId = "my-store",
            Name = "My Store",
            DomainPatternsJson = "[]",
            SelectorsJson = "{}",
            AffiliateParamName = "ref",
            AffiliateTag = "ophi-20"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items[0].AffiliateUrl.Should().BeNull();
    }

    // --- AtLowest Filter Tests ---

    [Fact]
    public async Task Handle_WithAtLowestTrue_ReturnsOnlyProductsAtLowestPrice()
    {
        // Arrange — Product A at lowest (50 == min of [50, 80, 100]), Product B NOT (90 != min of [50, 80, 90]), Product C at lowest (30 == min of [30, 30, 50])
        var productA = CreateProduct("Product A", "https://example.com/a");
        productA.CurrentPrice = 50m;
        var productB = CreateProduct("Product B", "https://example.com/b");
        productB.CurrentPrice = 90m;
        var productC = CreateProduct("Product C", "https://example.com/c");
        productC.CurrentPrice = 30m;
        _dbContext.Products.AddRange(productA, productB, productC);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var urlA = productA.ProductUrls.First().Id;
        var urlB = productB.ProductUrls.First().Id;
        var urlC = productC.ProductUrls.First().Id;
        _dbContext.PricePoints.AddRange(
            new PricePoint { Id = Guid.NewGuid(), ProductId = productA.Id, ProductUrlId = urlA, Price = 50m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-2) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = productA.Id, ProductUrlId = urlA, Price = 80m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-1) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = productA.Id, ProductUrlId = urlA, Price = 100m, Currency = "USD", RecordedAt = DateTime.UtcNow },
            new PricePoint { Id = Guid.NewGuid(), ProductId = productB.Id, ProductUrlId = urlB, Price = 50m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-2) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = productB.Id, ProductUrlId = urlB, Price = 80m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-1) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = productB.Id, ProductUrlId = urlB, Price = 90m, Currency = "USD", RecordedAt = DateTime.UtcNow },
            new PricePoint { Id = Guid.NewGuid(), ProductId = productC.Id, ProductUrlId = urlC, Price = 30m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-2) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = productC.Id, ProductUrlId = urlC, Price = 30m, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-1) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = productC.Id, ProductUrlId = urlC, Price = 50m, Currency = "USD", RecordedAt = DateTime.UtcNow }
        );
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, AtLowest: true);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items.Select(p => p.Name).Should().BeEquivalentTo("Product A", "Product C");
        result.Total.Should().Be(2);
    }

    [Fact]
    public async Task Handle_WithAtLowestFalse_ReturnsAllProducts()
    {
        // Arrange
        var productA = CreateProduct("Product A", "https://example.com/a");
        productA.CurrentPrice = 50m;
        var productB = CreateProduct("Product B", "https://example.com/b");
        productB.CurrentPrice = 90m;
        _dbContext.Products.AddRange(productA, productB);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var urlA = productA.ProductUrls.First().Id;
        var urlB = productB.ProductUrls.First().Id;
        _dbContext.PricePoints.AddRange(
            new PricePoint { Id = Guid.NewGuid(), ProductId = productA.Id, ProductUrlId = urlA, Price = 50m, Currency = "USD", RecordedAt = DateTime.UtcNow },
            new PricePoint { Id = Guid.NewGuid(), ProductId = productB.Id, ProductUrlId = urlB, Price = 50m, Currency = "USD", RecordedAt = DateTime.UtcNow }
        );
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, AtLowest: false);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithAtLowestTrue_ExcludesNullCurrentPrice()
    {
        // Arrange — product with null CurrentPrice but has PricePoints
        var product = CreateProduct("Null Price", "https://example.com/np");
        product.CurrentPrice = null;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.PricePoints.Add(new PricePoint
        {
            Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = product.ProductUrls.First().Id,
            Price = 50m, Currency = "USD", RecordedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, AtLowest: true);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithAtLowestTrue_ExcludesProductsWithNoPricePoints()
    {
        // Arrange — product with CurrentPrice set but no PricePoints
        var product = CreateProduct("No History", "https://example.com/nh");
        product.CurrentPrice = 50m;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, AtLowest: true);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithAtLowestTrue_WorksWithPagination()
    {
        // Arrange — 5 products all at their lowest, pageSize = 2
        for (var i = 0; i < 5; i++)
        {
            var product = CreateProduct($"Product {i}", $"https://example.com/{i}");
            product.CurrentPrice = 10m + i;
            _dbContext.Products.Add(product);
            await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            _dbContext.PricePoints.AddRange(
                new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = product.ProductUrls.First().Id, Price = 10m + i, Currency = "USD", RecordedAt = DateTime.UtcNow.AddDays(-1) },
                new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = product.ProductUrls.First().Id, Price = 100m, Currency = "USD", RecordedAt = DateTime.UtcNow }
            );
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, AtLowest: true, Page: 1, PageSize: 2);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Total.Should().Be(5);
    }

    [Fact]
    public async Task Handle_WithAtLowestTrue_CombinesWithStatusFilter()
    {
        // Arrange — 2 active at lowest, 1 paused at lowest
        var active1 = CreateProduct("Active 1", "https://example.com/a1", status: ProductStatus.Active);
        active1.CurrentPrice = 50m;
        var active2 = CreateProduct("Active 2", "https://example.com/a2", status: ProductStatus.Active);
        active2.CurrentPrice = 30m;
        var paused = CreateProduct("Paused", "https://example.com/p1", status: ProductStatus.Paused);
        paused.CurrentPrice = 20m;
        _dbContext.Products.AddRange(active1, active2, paused);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.PricePoints.AddRange(
            new PricePoint { Id = Guid.NewGuid(), ProductId = active1.Id, ProductUrlId = active1.ProductUrls.First().Id, Price = 50m, Currency = "USD", RecordedAt = DateTime.UtcNow },
            new PricePoint { Id = Guid.NewGuid(), ProductId = active2.Id, ProductUrlId = active2.ProductUrls.First().Id, Price = 30m, Currency = "USD", RecordedAt = DateTime.UtcNow },
            new PricePoint { Id = Guid.NewGuid(), ProductId = paused.Id, ProductUrlId = paused.ProductUrls.First().Id, Price = 20m, Currency = "USD", RecordedAt = DateTime.UtcNow }
        );
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, AtLowest: true, Status: "active");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items.Select(p => p.Name).Should().BeEquivalentTo("Active 1", "Active 2");
    }

    [Fact]
    public async Task Handle_ReturnsAtLowestCount_Always()
    {
        // Arrange — 2 at lowest, 1 not — count should be returned even without AtLowest filter
        var atLowest1 = CreateProduct("At Lowest 1", "https://example.com/al1");
        atLowest1.CurrentPrice = 50m;
        var atLowest2 = CreateProduct("At Lowest 2", "https://example.com/al2");
        atLowest2.CurrentPrice = 30m;
        var notLowest = CreateProduct("Not Lowest", "https://example.com/nl");
        notLowest.CurrentPrice = 90m;
        _dbContext.Products.AddRange(atLowest1, atLowest2, notLowest);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.PricePoints.AddRange(
            new PricePoint { Id = Guid.NewGuid(), ProductId = atLowest1.Id, ProductUrlId = atLowest1.ProductUrls.First().Id, Price = 50m, Currency = "USD", RecordedAt = DateTime.UtcNow },
            new PricePoint { Id = Guid.NewGuid(), ProductId = atLowest2.Id, ProductUrlId = atLowest2.ProductUrls.First().Id, Price = 30m, Currency = "USD", RecordedAt = DateTime.UtcNow },
            new PricePoint { Id = Guid.NewGuid(), ProductId = notLowest.Id, ProductUrlId = notLowest.ProductUrls.First().Id, Price = 50m, Currency = "USD", RecordedAt = DateTime.UtcNow }
        );
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(3);
        result.AtLowestCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_AtLowestCount_IsGlobalNotFiltered()
    {
        // Arrange — 2 at lowest (1 active, 1 paused), 1 not lowest — count should ignore status filter
        var activeAtLowest = CreateProduct("Active At Lowest", "https://example.com/aal", status: ProductStatus.Active);
        activeAtLowest.CurrentPrice = 50m;
        var pausedAtLowest = CreateProduct("Paused At Lowest", "https://example.com/pal", status: ProductStatus.Paused);
        pausedAtLowest.CurrentPrice = 30m;
        var notLowest = CreateProduct("Not Lowest", "https://example.com/nl", status: ProductStatus.Active);
        notLowest.CurrentPrice = 90m;
        _dbContext.Products.AddRange(activeAtLowest, pausedAtLowest, notLowest);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.PricePoints.AddRange(
            new PricePoint { Id = Guid.NewGuid(), ProductId = activeAtLowest.Id, ProductUrlId = activeAtLowest.ProductUrls.First().Id, Price = 50m, Currency = "USD", RecordedAt = DateTime.UtcNow },
            new PricePoint { Id = Guid.NewGuid(), ProductId = pausedAtLowest.Id, ProductUrlId = pausedAtLowest.ProductUrls.First().Id, Price = 30m, Currency = "USD", RecordedAt = DateTime.UtcNow },
            new PricePoint { Id = Guid.NewGuid(), ProductId = notLowest.Id, ProductUrlId = notLowest.ProductUrls.First().Id, Price = 50m, Currency = "USD", RecordedAt = DateTime.UtcNow }
        );
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Query with status filter — atLowestCount should still be 2 (global, not filtered)
        var query = new GetProducts.Query(_testUserId, Status: "active");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCount(2); // only active products
        result.AtLowestCount.Should().Be(2); // global count: both at-lowest products regardless of status
    }

    // --- Favourite / PriceDrop / HasAlerts filters (server-side dashboard filters, UX-2) ---

    [Fact]
    public async Task Handle_WithFavouriteTrue_ReturnsOnlyFavourites()
    {
        // Arrange
        var favourite = CreateProduct("Favourite", "https://example.com/fav");
        favourite.IsFavourite = true;
        var plain = CreateProduct("Plain", "https://example.com/plain");
        _dbContext.Products.AddRange(favourite, plain);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Favourite: true);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().ContainSingle(p => p.Name == "Favourite");
        result.Total.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithPriceDropTrue_ReturnsOnlyProductsWithNegativePriceChange()
    {
        // Arrange
        var dropped = CreateProduct("Dropped", "https://example.com/drop");
        dropped.CurrentPrice = 80m;
        dropped.PreviousPrice = 100m;
        var risen = CreateProduct("Risen", "https://example.com/rise");
        risen.CurrentPrice = 120m;
        risen.PreviousPrice = 100m;
        var noHistory = CreateProduct("No History", "https://example.com/none");
        noHistory.CurrentPrice = 50m;
        _dbContext.Products.AddRange(dropped, risen, noHistory);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, PriceDrop: true);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().ContainSingle(p => p.Name == "Dropped");
        result.Total.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithHasAlertsTrue_ReturnsOnlyProductsWithAlerts()
    {
        // Arrange
        var withAlert = CreateProduct("With Alert", "https://example.com/alert");
        var without = CreateProduct("Without Alert", "https://example.com/noalert");
        _dbContext.Products.AddRange(withAlert, without);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.Alerts.Add(new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = withAlert.Id,
            UserId = _testUserId,
            TargetPrice = 50m,
            ReferencePrice = 100m,
            Condition = AlertCondition.Below,
            IsActive = true
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, HasAlerts: true);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().ContainSingle(p => p.Name == "With Alert");
        result.Total.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ReturnsPriceDropAndWithAlertsCounts_Always()
    {
        // Arrange — 1 dropped, 1 risen, 1 with two alerts (counts products, not alert rows)
        var dropped = CreateProduct("Dropped", "https://example.com/drop");
        dropped.CurrentPrice = 80m;
        dropped.PreviousPrice = 100m;
        var risen = CreateProduct("Risen", "https://example.com/rise");
        risen.CurrentPrice = 120m;
        risen.PreviousPrice = 100m;
        var alerted = CreateProduct("Alerted", "https://example.com/alerted");
        _dbContext.Products.AddRange(dropped, risen, alerted);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.Alerts.AddRange(
            new Alert { Id = Guid.NewGuid(), ProductId = alerted.Id, UserId = _testUserId, TargetPrice = 50m, ReferencePrice = 100m, Condition = AlertCondition.Below, IsActive = true },
            new Alert { Id = Guid.NewGuid(), ProductId = alerted.Id, UserId = _testUserId, TargetPrice = 40m, ReferencePrice = 100m, Condition = AlertCondition.Below, IsActive = true });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.PriceDropCount.Should().Be(1);
        result.WithAlertsCount.Should().Be(1); // one product, despite two alert rows
    }

    [Fact]
    public async Task Handle_PriceDropAndWithAlertsCounts_AreGlobalNotFiltered()
    {
        // Arrange — the dropped/alerted products are paused; an active product matches neither
        var dropped = CreateProduct("Dropped Paused", "https://example.com/drop", status: ProductStatus.Paused);
        dropped.CurrentPrice = 80m;
        dropped.PreviousPrice = 100m;
        var active = CreateProduct("Active Plain", "https://example.com/plain");
        _dbContext.Products.AddRange(dropped, active);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.Alerts.Add(new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = dropped.Id,
            UserId = _testUserId,
            TargetPrice = 50m,
            ReferencePrice = 100m,
            Condition = AlertCondition.Below,
            IsActive = true
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, Status: "active");

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().ContainSingle(p => p.Name == "Active Plain");
        result.PriceDropCount.Should().Be(1); // global, ignores the status filter
        result.WithAlertsCount.Should().Be(1);
    }

    private Product CreateProduct(
        string name,
        string url,
        string? storeId = null,
        decimal? currentPrice = null,
        ProductStatus status = ProductStatus.Active)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = name,
            Currency = "USD",
            Status = status,
            CurrentPrice = currentPrice
        };
        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = url,
            Currency = "USD",
            StoreId = storeId
        };
        product.ProductUrls.Add(productUrl);
        _dbContext.ProductUrls.Add(productUrl);
        return product;
    }

    private static PricePoint CreatePricePoint(Product product, decimal price, int daysAgo = 0) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ProductUrlId = product.ProductUrls.First().Id,
            Price = price,
            Currency = "USD",
            RecordedAt = DateTime.UtcNow.AddDays(-daysAgo)
        };

    [Fact]
    public async Task Handle_WithTiedSortValues_OrdersDeterministicallyById()
    {
        // Pins the deterministic ordering contract: tied sort values are broken by Id. Note this
        // assertion is only meaningful on providers whose Id ordering matches .NET's Guid ordering —
        // SQLite (TEXT ids) does, Postgres (uuid, byte-ordered) does not, which is why the paging
        // guard for this lives in Ophi.Postgres.Tests rather than being asserted here.
        var products = Enumerable.Range(0, 5)
            .Select(i => new Product
            {
                Id = Guid.NewGuid(),
                UserId = _testUserId,
                Name = $"Tied Product {i}",
                Currency = "USD",
                Status = ProductStatus.Active,
                CurrentPrice = 9.99m // identical across all rows
            })
            .ToList();
        _dbContext.Products.AddRange(products);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetProducts.Query(_testUserId, SortBy: "price", SortDirection: "asc", PageSize: 10);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Items.Select(i => i.Id).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Handle_PagingThroughTiedSortValues_YieldsEveryProductExactlyOnce()
    {
        var products = Enumerable.Range(0, 6)
            .Select(i => new Product
            {
                Id = Guid.NewGuid(),
                UserId = _testUserId,
                Name = $"Tied Product {i}",
                Currency = "USD",
                Status = ProductStatus.Active,
                CurrentPrice = 9.99m
            })
            .ToList();
        _dbContext.Products.AddRange(products);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act — walk all three pages
        var seen = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await _handler.Handle(
                new GetProducts.Query(_testUserId, SortBy: "price", SortDirection: "asc", Page: page, PageSize: 2),
                TestContext.Current.CancellationToken);
            seen.AddRange(result.Items.Select(i => i.Id));
        }

        // Assert — no duplicates across pages, and nothing dropped
        seen.Should().OnlyHaveUniqueItems();
        seen.Should().BeEquivalentTo(products.Select(p => p.Id));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
