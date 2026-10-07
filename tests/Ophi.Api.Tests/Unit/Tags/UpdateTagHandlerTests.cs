using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Tags;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Tags;

public class UpdateTagHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly UpdateTag.Handler _handler;
    private readonly Guid _testUserId;

    public UpdateTagHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new UpdateTag.Handler(_dbContext, NullLogger<UpdateTag.Handler>.Instance);
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
    public async Task Handle_WithValidData_UpdatesAndReturnsTag()
    {
        // Arrange
        var tag = CreateTag("Electronics");
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateTag.Command(tag.Id, "Updated", "#FF0000", 5) { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Id.Should().Be(tag.Id);
        result.Name.Should().Be("Updated");
        result.Color.Should().Be("#FF0000");
        result.Weight.Should().Be(5);
    }

    [Fact]
    public async Task Handle_WithValidData_SavesChangesToDatabase()
    {
        // Arrange
        var tag = CreateTag("Electronics");
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateTag.Command(tag.Id, "Updated", "#FF0000", 5) { UserId = _testUserId };

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var savedTag = await _dbContext.Tags.FindAsync([tag.Id], TestContext.Current.CancellationToken);
        savedTag!.Name.Should().Be("Updated");
        savedTag.Color.Should().Be("#FF0000");
        savedTag.Weight.Should().Be(5);
    }

    [Fact]
    public async Task Handle_WithOnlyNameChange_UpdatesOnlyName()
    {
        // Arrange
        var tag = CreateTag("Electronics", "#3B82F6", 10);
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateTag.Command(tag.Id, "Updated", null, null) { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Name.Should().Be("Updated");
        result.Color.Should().Be("#3B82F6");
        result.Weight.Should().Be(10);
    }

    [Fact]
    public async Task Handle_WithOnlyColorChange_UpdatesOnlyColor()
    {
        // Arrange
        var tag = CreateTag("Electronics", "#3B82F6", 10);
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateTag.Command(tag.Id, null, "#FF0000", null) { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Name.Should().Be("Electronics");
        result.Color.Should().Be("#FF0000");
        result.Weight.Should().Be(10);
    }

    [Fact]
    public async Task Handle_WithOnlyWeightChange_UpdatesOnlyWeight()
    {
        // Arrange
        var tag = CreateTag("Electronics", "#3B82F6", 10);
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateTag.Command(tag.Id, null, null, 20) { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Name.Should().Be("Electronics");
        result.Color.Should().Be("#3B82F6");
        result.Weight.Should().Be(20);
    }

    [Fact]
    public async Task Handle_WithNonExistentTag_ThrowsNotFoundException()
    {
        // Arrange
        var command = new UpdateTag.Command(Guid.NewGuid(), "Updated", null, null) { UserId = _testUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Tag not found");
    }

    [Fact]
    public async Task Handle_WithOtherUsersTag_ThrowsNotFoundException()
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

        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Other's Tag",
            Color = "#3B82F6",
            Weight = 0
        };
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateTag.Command(tag.Id, "Updated", null, null) { UserId = _testUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Tag not found");
    }

    [Fact]
    public async Task Handle_WithDuplicateName_ThrowsApiException()
    {
        // Arrange
        var tag1 = CreateTag("Tag 1");
        var tag2 = CreateTag("Tag 2");
        _dbContext.Tags.AddRange(tag1, tag2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateTag.Command(tag2.Id, "Tag 1", null, null) { UserId = _testUserId };

        // Act
        var act = async () => await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>()
            .WithMessage("A tag with this name already exists");
    }

    [Fact]
    public async Task Handle_WithSameName_AllowsUpdate()
    {
        // Arrange
        var tag = CreateTag("Electronics");
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateTag.Command(tag.Id, "Electronics", "#FF0000", null) { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Name.Should().Be("Electronics");
        result.Color.Should().Be("#FF0000");
    }

    private Tag CreateTag(string name, string color = "#3B82F6", int weight = 0)
    {
        return new Tag
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = name,
            Color = color,
            Weight = weight
        };
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
