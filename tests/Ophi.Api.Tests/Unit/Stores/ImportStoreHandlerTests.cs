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

public class ImportStoreHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Mock<IStoreConfigProvider> _configProviderMock = new();
    private readonly ImportStore.Handler _handler;
    private readonly Guid _testUserId;

    public ImportStoreHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new ImportStore.Handler(_dbContext, _configProviderMock.Object, NullLogger<ImportStore.Handler>.Instance);
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
    public async Task Handle_WithValidImport_CreatesStore()
    {
        // Arrange
        var command = new ImportStore.Command(
            "imported-store",
            "Imported Store",
            ["imported.com"],
            new CreateStore.StoreSelectorDto(
                [".price"],
                [".name"],
                [".img"],
                null,
                null
            )
        )
        { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        result.StoreId.Should().Be("imported-store");
        result.Name.Should().Be("Imported Store");

        var stored = await _dbContext.StoreConfigurations
            .FirstOrDefaultAsync(s => s.StoreId == "imported-store" && s.UserId == _testUserId, cancellationToken: TestContext.Current.CancellationToken);
        stored.Should().NotBeNull();
        stored.Name.Should().Be("Imported Store");
    }

    [Fact]
    public async Task Handle_WithDuplicateStoreId_ThrowsConflict()
    {
        // Arrange
        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            StoreId = "existing-store",
            Name = "Existing Store",
            DomainPatternsJson = "[]",
            SelectorsJson = "{}",
            UserId = _testUserId
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new ImportStore.Command(
            "existing-store",
            "Another Store",
            ["another.com"],
            new CreateStore.StoreSelectorDto([".price"], [".name"], [".img"], null, null)
        )
        { UserId = _testUserId };

        // Act
        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>()
            .Where(e => e.StatusCode == 409);
    }

    [Fact]
    public async Task Handle_WithDuplicateDomainPatterns_Rejects()
    {
        // Arrange
        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            StoreId = "existing-store",
            Name = "Existing Store",
            DomainPatternsJson = JsonSerializer.Serialize(new[] { "overlap.com" }),
            SelectorsJson = "{}",
            UserId = _testUserId
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new ImportStore.Command(
            "new-store",
            "New Store",
            ["overlap.com"],
            new CreateStore.StoreSelectorDto([".price"], [".name"], [".img"], null, null)
        )
        { UserId = _testUserId };

        // Act
        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>()
            .Where(e => e.StatusCode == 409);
    }

    [Fact]
    public async Task Handle_WithDomainPatternDifferingOnlyInCase_ThrowsConflict()
    {
        // Arrange — existing store uses lowercase; import uses uppercase variant
        _dbContext.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            StoreId = "existing-store",
            Name = "Existing Store",
            DomainPatternsJson = JsonSerializer.Serialize(new[] { "overlap.com" }),
            SelectorsJson = "{}",
            UserId = _testUserId
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new ImportStore.Command(
            "new-store",
            "New Store",
            ["OVERLAP.COM"],
            new CreateStore.StoreSelectorDto([".price"], [".name"], [".img"], null, null)
        )
        { UserId = _testUserId };

        // Act
        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ApiException>()
            .Where(e => e.StatusCode == 409);
    }

    [Fact]
    public async Task Handle_RoundTrip_ExportThenImport()
    {
        // Arrange - create a store, export it, delete it, re-import
        var selectors = new StoreSelectorConfig
        {
            PriceSelectors = [".price"],
            NameSelectors = ["h1"],
            ImageSelectors = ["img.product"],
            PriceRegexPatterns = [@"\$[\d.]+"]
        };

        var originalStore = new StoreConfiguration
        {
            Id = Guid.NewGuid(),
            StoreId = "roundtrip-store",
            Name = "Roundtrip Store",
            DomainPatternsJson = JsonSerializer.Serialize(new[] { "roundtrip.com" }),
            SelectorsJson = JsonSerializer.Serialize(selectors),
            PriceLocale = "en-GB",
            RequiresJavaScript = true,
            UserId = _testUserId
        };
        _dbContext.StoreConfigurations.Add(originalStore);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Export
        var exportHandler = new ExportStore.Handler(_dbContext, NullLogger<ExportStore.Handler>.Instance);
        var exported = await exportHandler.Handle(
            new ExportStore.Query(originalStore.Id, _testUserId), TestContext.Current.CancellationToken);

        // Delete original
        _dbContext.StoreConfigurations.Remove(originalStore);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Import using exported data
        var importCommand = new ImportStore.Command(
            exported.StoreId,
            exported.Name,
            exported.DomainPatterns,
            new CreateStore.StoreSelectorDto(
                exported.Selectors.PriceSelectors,
                exported.Selectors.NameSelectors,
                exported.Selectors.ImageSelectors,
                exported.Selectors.PriceRegexPatterns,
                exported.Selectors.ImageRegexPatterns
            ),
            exported.PriceLocale,
            exported.RequiresJavaScript
        )
        { UserId = _testUserId };

        // Act
        var result = await _handler.Handle(importCommand, TestContext.Current.CancellationToken);

        // Assert
        result.StoreId.Should().Be("roundtrip-store");
        result.Name.Should().Be("Roundtrip Store");

        var reimported = await _dbContext.StoreConfigurations
            .FirstOrDefaultAsync(s => s.StoreId == "roundtrip-store" && s.UserId == _testUserId, cancellationToken: TestContext.Current.CancellationToken);
        reimported.Should().NotBeNull();
        reimported.PriceLocale.Should().Be("en-GB");
        reimported.RequiresJavaScript.Should().BeTrue();

        var reimportedSelectors = JsonSerializer.Deserialize<StoreSelectorConfig>(reimported.SelectorsJson);
        reimportedSelectors!.PriceSelectors.Should().BeEquivalentTo([".price"]);
        reimportedSelectors.PriceRegexPatterns.Should().BeEquivalentTo([@"\$[\d.]+"]);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
