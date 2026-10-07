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

public class AddProductToGroupHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly AddProductToGroup.Handler _handler;
    private readonly Guid _testUserId;

    public AddProductToGroupHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new AddProductToGroup.Handler(_dbContext, NullLogger<AddProductToGroup.Handler>.Instance);
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
    public async Task Handle_WithValidData_AddsProductToGroup()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        var product = CreateProduct("Sony Headphones");
        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddProductToGroup.Command(group.Id, product.Id) { UserId = _testUserId };

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.ComparisonGroupId.Should().Be(group.Id);
    }

    [Fact]
    public async Task Handle_WithNonExistentGroup_ThrowsNotFoundException()
    {
        // Arrange
        var product = CreateProduct("Sony Headphones");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddProductToGroup.Command(Guid.NewGuid(), product.Id) { UserId = _testUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Comparison group not found");
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ThrowsNotFoundException()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        _dbContext.ComparisonGroups.Add(group);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddProductToGroup.Command(group.Id, Guid.NewGuid()) { UserId = _testUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Product not found");
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

        var product = CreateProduct("Sony Headphones");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddProductToGroup.Command(otherGroup.Id, product.Id) { UserId = _testUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WithProductBelongingToDifferentUser_ThrowsNotFoundException()
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

        var group = CreateGroup("Headphones Comparison");
        _dbContext.ComparisonGroups.Add(group);

        var otherProduct = new Product
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Other's Headphones",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(otherProduct);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddProductToGroup.Command(group.Id, otherProduct.Id) { UserId = _testUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenProductAlreadyInAnotherGroup_MovesProductToNewGroup()
    {
        // Arrange
        var group1 = CreateGroup("Group 1");
        var group2 = CreateGroup("Group 2");
        var product = CreateProduct("Sony Headphones");
        product.ComparisonGroupId = group1.Id;

        _dbContext.ComparisonGroups.AddRange(group1, group2);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddProductToGroup.Command(group2.Id, product.Id) { UserId = _testUserId };

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.ComparisonGroupId.Should().Be(group2.Id);
    }

    [Fact]
    public async Task Handle_WhenProductAlreadyInSameGroup_DoesNotThrowError()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        var product = CreateProduct("Sony Headphones");
        product.ComparisonGroupId = group.Id;

        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddProductToGroup.Command(group.Id, product.Id) { UserId = _testUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().NotThrowAsync();
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.ComparisonGroupId.Should().Be(group.Id);
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

    private Product CreateProduct(string name) =>
        TestEntityFactory.Product(_testUserId).Named(name).Build();

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
