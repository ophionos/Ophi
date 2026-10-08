using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Stores;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Api.Tests.Unit.Stores;

public class CreateStoreHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Mock<IStoreConfigProvider> _configProviderMock = new();
    private readonly CreateStore.Handler _handler;
    private readonly Guid _testUserId;

    public CreateStoreHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new CreateStore.Handler(_dbContext, _configProviderMock.Object, NullLogger<CreateStore.Handler>.Instance);
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
    public async Task Handle_WithValidData_CreatesStore()
    {
        // Arrange
        var command = new CreateStore.Command(
            "test-store",
            "Test Store",
            ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null)
        )
        { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.StoreId.Should().Be("test-store");
        result.Name.Should().Be("Test Store");
        result.IsBuiltIn.Should().BeFalse();
        result.IsAutoCreated.Should().BeFalse();
        result.PriceLocale.Should().Be("en-US");
        result.RequiresJavaScript.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithDuplicateStoreId_Throws409()
    {
        // Arrange — seed an existing store
        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            StoreId = "my-store",
            Name = "Existing",
            DomainPatternsJson = JsonSerializer.Serialize(new[] { "existing.com" }),
            SelectorsJson = "{}",
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateStore.Command(
            "my-store",
            "Duplicate",
            ["other.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null)
        )
        { UserId = _testUserId };

        // Act
        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_WithDuplicateDomainPattern_Throws409()
    {
        // Arrange — seed an existing store with "example.com"
        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            StoreId = "existing-store",
            Name = "Existing Store",
            DomainPatternsJson = JsonSerializer.Serialize(new[] { "example.com" }),
            SelectorsJson = "{}",
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateStore.Command(
            "new-store",
            "New Store",
            ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null)
        )
        { UserId = _testUserId };

        // Act
        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.StatusCode.Should().Be(409);
        ex.Which.Message.Should().Contain("example.com");
        ex.Which.Message.Should().Contain("existing-store");
    }

    [Fact]
    public async Task Handle_WithDuplicateDomainPattern_CaseInsensitive_Throws409()
    {
        // Arrange
        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            StoreId = "existing-store",
            Name = "Existing Store",
            DomainPatternsJson = JsonSerializer.Serialize(new[] { "Example.COM" }),
            SelectorsJson = "{}",
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateStore.Command(
            "new-store",
            "New Store",
            ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null)
        )
        { UserId = _testUserId };

        // Act
        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>();
    }

    [Fact]
    public async Task Handle_WithNonOverlappingDomains_Succeeds()
    {
        // Arrange
        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            StoreId = "existing-store",
            Name = "Existing Store",
            DomainPatternsJson = JsonSerializer.Serialize(new[] { "existing.com" }),
            SelectorsJson = "{}",
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateStore.Command(
            "new-store",
            "New Store",
            ["different.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null)
        )
        { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.StoreId.Should().Be("new-store");
    }

    [Fact]
    public async Task Handle_DuplicateDomainFromDifferentUser_Succeeds()
    {
        // Arrange — another user owns "example.com"
        var otherUserId = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = otherUserId,
            Email = "other@example.com",
            Name = "Other User",
            PasswordHash = "hash"
        });
        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            StoreId = "other-store",
            Name = "Other Store",
            DomainPatternsJson = JsonSerializer.Serialize(new[] { "example.com" }),
            SelectorsJson = "{}",
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CreateStore.Command(
            "my-store",
            "My Store",
            ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null)
        )
        { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert — different users can have the same domains
        result.StoreId.Should().Be("my-store");
    }

    [Fact]
    public async Task Handle_WithCurrencyOverride_SetsCurrencyOverride()
    {
        // Arrange
        var command = new CreateStore.Command(
            "test-store",
            "Test Store",
            ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null),
            "en-US",
            false,
            "EUR"
        )
        { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.CurrencyOverride.Should().Be("EUR");
    }

    [Fact]
    public async Task Handle_WithNullCurrencyOverride_ReturnsNullCurrencyOverride()
    {
        // Arrange
        var command = new CreateStore.Command(
            "test-store",
            "Test Store",
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
    public async Task Handle_WithAffiliateFields_PersistsAffiliateConfig()
    {
        // Arrange
        var command = new CreateStore.Command(
            "affiliate-store",
            "Affiliate Store",
            ["affiliate.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null),
            "en-US",
            false,
            null,
            "tag",
            "ophi-20"
        )
        { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.AffiliateParamName.Should().Be("tag");
        result.AffiliateTag.Should().Be("ophi-20");
    }

    [Fact]
    public async Task Handle_WithNoAffiliateFields_ReturnsNullAffiliateConfig()
    {
        // Arrange
        var command = new CreateStore.Command(
            "no-affiliate-store",
            "No Affiliate Store",
            ["noaffiliate.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null)
        )
        { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.AffiliateParamName.Should().BeNull();
        result.AffiliateTag.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithCustomUserAgent_PersistsAndReturnsUA()
    {
        // Arrange
        const string customUa = "Mozilla/5.0 CustomBrowser/1.0";
        var command = new CreateStore.Command(
            "custom-ua-store",
            "Custom UA Store",
            ["custom-ua.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null),
            CustomUserAgent: customUa
        )
        { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.CustomUserAgent.Should().Be(customUa);

        var stored = await _dbContext.StoreConfigurations
            .FirstAsync(s => s.StoreId == "custom-ua-store", TestContext.Current.CancellationToken);
        stored.CustomUserAgent.Should().Be(customUa);
    }

    [Fact]
    public async Task Handle_WithoutCustomUserAgent_ReturnsNullUA()
    {
        // Arrange
        var command = new CreateStore.Command(
            "no-ua-store",
            "No UA Store",
            ["no-ua.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null)
        )
        { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.CustomUserAgent.Should().BeNull();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
