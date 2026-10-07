using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Tags;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Tags;

public class CreateTagHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly CreateTag.Handler _handler;
    private readonly Guid _testUserId;

    public CreateTagHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new CreateTag.Handler(_dbContext, NullLogger<CreateTag.Handler>.Instance);
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
    public async Task Handle_WithValidData_CreatesAndReturnsTag()
    {
        // Arrange
        var command = new CreateTag.Command("Electronics", "#3B82F6", 10) { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Name.Should().Be("Electronics");
        result.Color.Should().Be("#3B82F6");
        result.Weight.Should().Be(10);
        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_WithValidData_SavesTagToDatabase()
    {
        // Arrange
        var command = new CreateTag.Command("Electronics", "#3B82F6", 10) { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var savedTag = await _dbContext.Tags.FindAsync([result.Id], TestContext.Current.CancellationToken);
        savedTag.Should().NotBeNull();
        savedTag.Name.Should().Be("Electronics");
        savedTag.Color.Should().Be("#3B82F6");
        savedTag.Weight.Should().Be(10);
        savedTag.UserId.Should().Be(_testUserId);
    }

    [Fact]
    public async Task Handle_WithNullColor_UsesDefaultColor()
    {
        // Arrange
        var command = new CreateTag.Command("Electronics", null, null) { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Color.Should().Be("#3B82F6");
    }

    [Fact]
    public async Task Handle_WithNullWeight_UsesDefaultWeight()
    {
        // Arrange
        var command = new CreateTag.Command("Electronics", null, null) { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Weight.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithDuplicateName_ThrowsApiException()
    {
        // Arrange
        var existingTag = new Tag
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Electronics",
            Color = "#3B82F6",
            Weight = 0
        };
        _dbContext.Tags.Add(existingTag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateTag.Command("Electronics", null, null) { UserId = _testUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>()
            .WithMessage("A tag with this name already exists");
    }

    [Fact]
    public async Task Handle_WithSameNameDifferentUser_AllowsTagCreation()
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

        var existingTag = new Tag
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Electronics",
            Color = "#3B82F6",
            Weight = 0
        };
        _dbContext.Tags.Add(existingTag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateTag.Command("Electronics", null, null) { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Name.Should().Be("Electronics");
    }

    [Fact]
    public async Task Handle_GeneratesUniqueId()
    {
        // Arrange
        var command1 = new CreateTag.Command("Tag 1", null, null) { UserId = _testUserId };
        var command2 = new CreateTag.Command("Tag 2", null, null) { UserId = _testUserId };

        // Act
        var result1 = await _handler.Handle(command1, TestContext.Current.CancellationToken);
        var result2 = await _handler.Handle(command2, TestContext.Current.CancellationToken);

        // Assert
        result1.Id.Should().NotBe(result2.Id);
    }

    [Fact]
    public async Task Handle_WithMultipleTags_IncreasesCount()
    {
        // Arrange
        var initialCount = await _dbContext.Tags.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        var command = new CreateTag.Command("Electronics", null, null) { UserId = _testUserId };

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var finalCount = await _dbContext.Tags.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        finalCount.Should().Be(initialCount + 1);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}

