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

public class RemoveProductFromGroupHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly RemoveProductFromGroup.Handler _handler;
    private readonly Guid _testUserId;

    public RemoveProductFromGroupHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new RemoveProductFromGroup.Handler(_dbContext, NullLogger<RemoveProductFromGroup.Handler>.Instance);
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
    public async Task Handle_WithValidData_RemovesProductFromGroup()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        var product = CreateProduct("Sony Headphones");
        product.ComparisonGroupId = group.Id;

        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new RemoveProductFromGroup.Command(group.Id, product.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct!.ComparisonGroupId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithNonExistentGroup_ThrowsNotFoundException()
    {
        // Arrange
        var product = CreateProduct("Sony Headphones");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new RemoveProductFromGroup.Command(Guid.NewGuid(), product.Id, _testUserId);

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

        var command = new RemoveProductFromGroup.Command(group.Id, Guid.NewGuid(), _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Product not found");
    }

    [Fact]
    public async Task Handle_WithProductNotInGroup_ThrowsApiException()
    {
        // Arrange
        var group = CreateGroup("Headphones Comparison");
        var product = CreateProduct("Sony Headphones");
        // Product is NOT in the group

        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new RemoveProductFromGroup.Command(group.Id, product.Id, _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>()
            .WithMessage("Product is not in this comparison group");
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
        product.ComparisonGroupId = otherGroup.Id;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new RemoveProductFromGroup.Command(otherGroup.Id, product.Id, _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
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
