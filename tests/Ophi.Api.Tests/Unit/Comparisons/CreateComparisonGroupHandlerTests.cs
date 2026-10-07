using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Comparisons;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Comparisons;

public class CreateComparisonGroupHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly CreateComparisonGroup.Handler _handler;
    private readonly Guid _testUserId;

    public CreateComparisonGroupHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new CreateComparisonGroup.Handler(_dbContext, NullLogger<CreateComparisonGroup.Handler>.Instance);
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
    public async Task Handle_WithValidData_CreatesAndReturnsComparisonGroup()
    {
        // Arrange
        var command = new CreateComparisonGroup.Command("Headphones Comparison") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Name.Should().Be("Headphones Comparison");
        result.ProductCount.Should().Be(0);
        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_WithValidData_SavesComparisonGroupToDatabase()
    {
        // Arrange
        var command = new CreateComparisonGroup.Command("Headphones Comparison") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var savedGroup = await _dbContext.ComparisonGroups.FindAsync([result.Id], TestContext.Current.CancellationToken);
        savedGroup.Should().NotBeNull();
        savedGroup.Name.Should().Be("Headphones Comparison");
        savedGroup.UserId.Should().Be(_testUserId);
    }

    [Fact]
    public async Task Handle_WithDuplicateName_ThrowsApiException()
    {
        // Arrange
        var existingGroup = new ComparisonGroup
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Headphones Comparison"
        };
        _dbContext.ComparisonGroups.Add(existingGroup);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateComparisonGroup.Command("Headphones Comparison") { UserId = _testUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>()
            .WithMessage("A comparison group with this name already exists");
    }

    [Fact]
    public async Task Handle_WithSameNameDifferentUser_AllowsComparisonGroupCreation()
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

        var existingGroup = new ComparisonGroup
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Headphones Comparison"
        };
        _dbContext.ComparisonGroups.Add(existingGroup);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateComparisonGroup.Command("Headphones Comparison") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Name.Should().Be("Headphones Comparison");
    }

    [Fact]
    public async Task Handle_GeneratesUniqueId()
    {
        // Arrange
        var command1 = new CreateComparisonGroup.Command("Group 1") { UserId = _testUserId };
        var command2 = new CreateComparisonGroup.Command("Group 2") { UserId = _testUserId };

        // Act
        var result1 = await _handler.Handle(command1, TestContext.Current.CancellationToken);
        var result2 = await _handler.Handle(command2, TestContext.Current.CancellationToken);

        // Assert
        result1.Id.Should().NotBe(result2.Id);
    }

    [Fact]
    public async Task Handle_ReturnsZeroProductCount()
    {
        // Arrange
        var command = new CreateComparisonGroup.Command("Headphones Comparison") { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.ProductCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithDescription_SavesDescription()
    {
        // Arrange
        var command = new CreateComparisonGroup.Command("Headphones Comparison", "Compare Sony vs Bose vs Apple")
        {
            UserId = _testUserId
        };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var savedGroup = await _dbContext.ComparisonGroups.FindAsync([result.Id], TestContext.Current.CancellationToken);
        savedGroup!.Description.Should().Be("Compare Sony vs Bose vs Apple");
    }

    [Fact]
    public async Task Handle_WithMultipleGroups_IncreasesCount()
    {
        // Arrange
        var initialCount = await _dbContext.ComparisonGroups.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        var command = new CreateComparisonGroup.Command("Headphones Comparison") { UserId = _testUserId };

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var finalCount = await _dbContext.ComparisonGroups.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        finalCount.Should().Be(initialCount + 1);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
