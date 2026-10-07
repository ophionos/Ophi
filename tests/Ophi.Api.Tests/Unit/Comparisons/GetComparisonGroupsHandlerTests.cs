using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Api.Features.Comparisons;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Comparisons;

public class GetComparisonGroupsHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly GetComparisonGroups.Handler _handler;
    private readonly Guid _testUserId;

    public GetComparisonGroupsHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new GetComparisonGroups.Handler(_dbContext, NullLogger<GetComparisonGroups.Handler>.Instance);
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

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
