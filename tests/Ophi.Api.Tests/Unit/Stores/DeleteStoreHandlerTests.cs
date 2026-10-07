using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Moq;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Stores;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Api.Tests.Unit.Stores;

public class DeleteStoreHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Mock<IStoreConfigProvider> _configProviderMock = new();
    private readonly DeleteStore.Handler _handler;
    private readonly Guid _testUserId;

    public DeleteStoreHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new DeleteStore.Handler(_dbContext, _configProviderMock.Object, NullLogger<DeleteStore.Handler>.Instance);
        _testUserId = Guid.NewGuid();

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
    public async Task Handle_WithExistingStore_DeletesStore()
    {
        // Arrange
        var storeId = Guid.NewGuid();
        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = storeId,
            UserId = _testUserId,
            StoreId = "my-store",
            Name = "My Store",
            DomainPatternsJson = JsonSerializer.Serialize(new[] { "example.com" }),
            SelectorsJson = "{}",
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteStore.Command(storeId, _testUserId);

        // Act
        await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        _dbContext.StoreConfigurations.Should().BeEmpty();
        _configProviderMock.Verify(p => p.InvalidateCache(_testUserId), Times.Once);
    }

    [Fact]
    public async Task Handle_WithNonExistentStore_ThrowsNotFoundException()
    {
        // Arrange
        var command = new DeleteStore.Command(Guid.NewGuid(), _testUserId);

        // Act
        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WithStoreBelongingToDifferentUser_ThrowsNotFoundException()
    {
        // Arrange — store belongs to another user
        var otherUserId = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = otherUserId,
            Email = "other@example.com",
            Name = "Other User",
            PasswordHash = "hash"
        });

        var storeId = Guid.NewGuid();
        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = storeId,
            UserId = otherUserId,
            StoreId = "other-store",
            Name = "Other Store",
            DomainPatternsJson = JsonSerializer.Serialize(new[] { "other.com" }),
            SelectorsJson = "{}",
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DeleteStore.Command(storeId, _testUserId);

        // Act
        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
        _dbContext.StoreConfigurations.Should().HaveCount(1, "the store should not be deleted");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
