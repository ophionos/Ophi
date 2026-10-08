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

public class UpdateStoreHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Mock<IStoreConfigProvider> _configProviderMock = new();
    private readonly UpdateStore.Handler _handler;
    private readonly Guid _testUserId;

    public UpdateStoreHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new UpdateStore.Handler(_dbContext, _configProviderMock.Object, NullLogger<UpdateStore.Handler>.Instance);
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
    public async Task Handle_WithValidData_UpdatesStore()
    {
        // Arrange
        var storeId = Guid.NewGuid();
        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = storeId,
            UserId = _testUserId,
            StoreId = "my-store",
            Name = "Old Name",
            DomainPatternsJson = JsonSerializer.Serialize(new[] { "example.com" }),
            SelectorsJson = "{}",
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateStore.Command(
            storeId,
            "New Name",
            ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null),
            "pt-PT",
            true
        )
        { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.Name.Should().Be("New Name");
        result.PriceLocale.Should().Be("pt-PT");
        result.RequiresJavaScript.Should().BeTrue();
        result.IsBuiltIn.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithDuplicateDomainInOtherStore_Throws409()
    {
        // Arrange — two stores, update one to overlap with the other
        var storeAId = Guid.NewGuid();
        var storeBId = Guid.NewGuid();
        _dbContext.StoreConfigurations.AddRange(
            new StoreConfiguration
            {
                Id = storeAId,
                UserId = _testUserId,
                StoreId = "store-a",
                Name = "Store A",
                DomainPatternsJson = JsonSerializer.Serialize(new[] { "a.com" }),
                SelectorsJson = "{}",
            },
            new StoreConfiguration
            {
                Id = storeBId,
                UserId = _testUserId,
                StoreId = "store-b",
                Name = "Store B",
                DomainPatternsJson = JsonSerializer.Serialize(new[] { "b.com" }),
                SelectorsJson = "{}",
            }
        );
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Try to update store-a to also use b.com
        var command = new UpdateStore.Command(
            storeAId,
            "Store A",
            ["a.com", "b.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null)
        )
        { UserId = _testUserId };

        // Act
        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.StatusCode.Should().Be(409);
        ex.Which.Message.Should().Contain("b.com");
        ex.Which.Message.Should().Contain("store-b");
    }

    [Fact]
    public async Task Handle_KeepingOwnDomains_Succeeds()
    {
        // Arrange — updating a store but keeping its own domains should not conflict
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

        var command = new UpdateStore.Command(
            storeId,
            "Updated Name",
            ["example.com", "example.org"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null)
        )
        { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert — no conflict with itself
        result.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task Handle_StoreNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var command = new UpdateStore.Command(
            Guid.NewGuid(),
            "Name",
            ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null)
        )
        { UserId = _testUserId };

        // Act
        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WithCurrencyOverride_SetsCurrencyOverride()
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

        var command = new UpdateStore.Command(
            storeId,
            "My Store",
            ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null),
            null,
            null,
            "EUR"
        )
        { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.CurrencyOverride.Should().Be("EUR");
    }

    [Fact]
    public async Task Handle_WithNullCurrencyOverride_ClearsCurrencyOverride()
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
            CurrencyOverride = "EUR"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateStore.Command(
            storeId,
            "My Store",
            ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null)
        )
        { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.CurrencyOverride.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithAffiliateFields_UpdatesAffiliateConfig()
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

        var command = new UpdateStore.Command(
            storeId,
            "My Store",
            ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null),
            null,
            null,
            null,
            "ref",
            "my-tag-123"
        )
        { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.AffiliateParamName.Should().Be("ref");
        result.AffiliateTag.Should().Be("my-tag-123");
    }

    [Fact]
    public async Task Handle_WithNullAffiliateFields_ClearsAffiliateConfig()
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
            AffiliateParamName = "tag",
            AffiliateTag = "old-tag"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateStore.Command(
            storeId,
            "My Store",
            ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null)
        )
        { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.AffiliateParamName.Should().BeNull();
        result.AffiliateTag.Should().BeNull();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
