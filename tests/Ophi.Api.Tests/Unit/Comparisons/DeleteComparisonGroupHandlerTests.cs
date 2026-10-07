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

public class DeleteComparisonGroupHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly DeleteComparisonGroup.Handler _handler;
    private readonly Guid _testUserId;

    public DeleteComparisonGroupHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new DeleteComparisonGroup.Handler(_dbContext, NullLogger<DeleteComparisonGroup.Handler>.Instance);
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
    public async Task Handle_WithValidGroup_DeletesGroup()
    {
        // Arrange
        var group = new ComparisonGroup
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Headphones Comparison"
        };
        _dbContext.ComparisonGroups.Add(group);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteComparisonGroup.Command(group.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var deletedGroup = await _dbContext.ComparisonGroups.FindAsync([group.Id], TestContext.Current.CancellationToken);
        deletedGroup.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithNonExistentGroup_ThrowsNotFoundException()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var command = new DeleteComparisonGroup.Command(nonExistentId, _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

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

        var command = new DeleteComparisonGroup.Command(otherGroup.Id, _testUserId);

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WithGroupContainingProducts_SetsProductComparisonGroupIdToNull()
    {
        // Arrange
        var group = new ComparisonGroup
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Headphones Comparison"
        };
        _dbContext.ComparisonGroups.Add(group);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            ComparisonGroupId = group.Id,
            Name = "Sony Headphones",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteComparisonGroup.Command(group.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var updatedProduct = await _dbContext.Products.FindAsync([product.Id], TestContext.Current.CancellationToken);
        updatedProduct.Should().NotBeNull();
        updatedProduct.ComparisonGroupId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_DecreasesGroupCount()
    {
        // Arrange
        var group = new ComparisonGroup
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Headphones Comparison"
        };
        _dbContext.ComparisonGroups.Add(group);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var initialCount = await _dbContext.ComparisonGroups.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        var command = new DeleteComparisonGroup.Command(group.Id, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var finalCount = await _dbContext.ComparisonGroups.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        finalCount.Should().Be(initialCount - 1);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
