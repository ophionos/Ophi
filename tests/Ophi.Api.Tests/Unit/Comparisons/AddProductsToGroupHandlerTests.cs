using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Comparisons;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Comparisons;

public class AddProductsToGroupHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly AddProductsToGroup.Handler _handler;
    private readonly Guid _testUserId;

    public AddProductsToGroupHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new AddProductsToGroup.Handler(_dbContext, NullLogger<AddProductsToGroup.Handler>.Instance);
        _testUserId = Guid.NewGuid();

        _dbContext.Users.Add(new User
        {
            Id = _testUserId,
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hash"
        });
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task Handle_WithMultipleProducts_AddsAllToGroup()
    {
        // Arrange
        var group = CreateGroup("Comparison");
        var p1 = CreateProduct("Product 1");
        var p2 = CreateProduct("Product 2");
        var p3 = CreateProduct("Product 3");
        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.AddRange(p1, p2, p3);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddProductsToGroup.Command(group.Id, [p1.Id, p2.Id, p3.Id]) { UserId = _testUserId };

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var grouped = await _dbContext.Products
            .Where(p => p.ComparisonGroupId == group.Id)
            .Select(p => p.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        grouped.Should().BeEquivalentTo([p1.Id, p2.Id, p3.Id]);
    }

    [Fact]
    public async Task Handle_WithNonExistentGroup_ThrowsNotFoundException()
    {
        var product = CreateProduct("Product 1");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddProductsToGroup.Command(Guid.NewGuid(), [product.Id]) { UserId = _testUserId };

        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>().WithMessage("Comparison group not found");
    }

    [Fact]
    public async Task Handle_IgnoresProductsNotOwnedByUser()
    {
        // Arrange
        var otherUserId = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = otherUserId,
            Email = "other@example.com",
            Name = "Other User",
            PasswordHash = "hash"
        });

        var group = CreateGroup("Comparison");
        var mine = CreateProduct("Mine");
        var foreignProduct = new Product
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Theirs",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.AddRange(mine, foreignProduct);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddProductsToGroup.Command(group.Id, [mine.Id, foreignProduct.Id]) { UserId = _testUserId };

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert — only the owned product is added; the foreign product is untouched.
        var mineUpdated = await _dbContext.Products.FindAsync([mine.Id], TestContext.Current.CancellationToken);
        mineUpdated!.ComparisonGroupId.Should().Be(group.Id);
        var foreignUpdated = await _dbContext.Products.FindAsync([foreignProduct.Id], TestContext.Current.CancellationToken);
        foreignUpdated!.ComparisonGroupId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithUnknownProductIds_IgnoresThemSilently()
    {
        var group = CreateGroup("Comparison");
        var product = CreateProduct("Product 1");
        _dbContext.ComparisonGroups.Add(group);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddProductsToGroup.Command(group.Id, [product.Id, Guid.NewGuid()]) { UserId = _testUserId };

        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
        var updated = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updated!.ComparisonGroupId.Should().Be(group.Id);
    }

    [Fact]
    public async Task Handle_MovesProductsAlreadyInAnotherGroup()
    {
        var group1 = CreateGroup("Group 1");
        var group2 = CreateGroup("Group 2");
        var product = CreateProduct("Product 1");
        product.ComparisonGroupId = group1.Id;
        _dbContext.ComparisonGroups.AddRange(group1, group2);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddProductsToGroup.Command(group2.Id, [product.Id]) { UserId = _testUserId };

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        var updated = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updated!.ComparisonGroupId.Should().Be(group2.Id);
    }

    private ComparisonGroup CreateGroup(string name) =>
        new() { Id = Guid.NewGuid(), UserId = _testUserId, Name = name };

    private Product CreateProduct(string name) =>
        TestEntityFactory.Product(_testUserId).Named(name).Build();

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
