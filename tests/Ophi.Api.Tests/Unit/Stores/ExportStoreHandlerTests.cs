using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Stores;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Api.Tests.Unit.Stores;

public class ExportStoreHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly ExportStore.Handler _handler;
    private readonly Guid _testUserId;

    public ExportStoreHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new ExportStore.Handler(_dbContext, NullLogger<ExportStore.Handler>.Instance);
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
    public async Task Handle_WithValidId_ReturnsExportDto()
    {
        // Arrange
        var selectors = new StoreSelectorConfig
        {
            PriceSelectors = [".price"],
            NameSelectors = ["h1"],
            ImageSelectors = ["img.product"],
            PriceRegexPatterns = [@"\$[\d.]+"],
            ImageRegexPatterns = null
        };

        var store = new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            StoreId = "my-store",
            Name = "My Store",
            DomainPatternsJson = JsonSerializer.Serialize(new[] { "mystore.com", "mystore.co.uk" }),
            SelectorsJson = JsonSerializer.Serialize(selectors),
            PriceLocale = "en-GB",
            RequiresJavaScript = true,
            UserId = _testUserId
        };
        _dbContext.StoreConfigurations.Add(store);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new ExportStore.Query(store.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        result.Version.Should().Be(1);
        result.StoreId.Should().Be("my-store");
        result.Name.Should().Be("My Store");
        result.DomainPatterns.Should().BeEquivalentTo(["mystore.com", "mystore.co.uk"]);
        result.Selectors.PriceSelectors.Should().BeEquivalentTo([".price"]);
        result.Selectors.NameSelectors.Should().BeEquivalentTo(["h1"]);
        result.Selectors.ImageSelectors.Should().BeEquivalentTo(["img.product"]);
        result.Selectors.PriceRegexPatterns.Should().BeEquivalentTo([@"\$[\d.]+"]);
        result.PriceLocale.Should().Be("en-GB");
        result.RequiresJavaScript.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithNonExistentStore_ThrowsNotFound()
    {
        // Arrange
        var query = new ExportStore.Query(Guid.NewGuid(), _testUserId);

        // Act
        var act = () => _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("Store not found");
    }

    [Fact]
    public async Task Handle_ExcludesInternalIds()
    {
        // Arrange
        var store = new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            StoreId = "export-store",
            Name = "Export Store",
            DomainPatternsJson = JsonSerializer.Serialize(new[] { "export.com" }),
            SelectorsJson = JsonSerializer.Serialize(new StoreSelectorConfig
            {
                PriceSelectors = [".price"],
                NameSelectors = [".name"],
                ImageSelectors = [".img"]
            }),
            UserId = _testUserId
        };
        _dbContext.StoreConfigurations.Add(store);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new ExportStore.Query(store.Id, _testUserId);

        // Act
        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert - serialize and verify no Id or UserId fields
        var json = JsonSerializer.Serialize(result);
        json.Should().NotContain("\"Id\"");
        json.Should().NotContain("\"UserId\"");
    }

    [Fact]
    public async Task Handle_WithOtherUsersStore_ThrowsNotFound()
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

        var store = new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            StoreId = "other-store",
            Name = "Other Store",
            DomainPatternsJson = "[]",
            SelectorsJson = "{}",
            UserId = otherUserId
        };
        _dbContext.StoreConfigurations.Add(store);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new ExportStore.Query(store.Id, _testUserId);

        // Act
        var act = () => _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
