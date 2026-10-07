using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Tests.Helpers;
using Ophi.TestHelpers;
using Ophi.Worker.Handlers;

namespace Ophi.Infrastructure.Tests.Handlers;

public class RecordPriceHistoryHandlerTests : IDisposable
{
    private readonly OphiDbContext _dbContext;
    private readonly SqliteConnection _connection;
    private readonly Guid _testUserId;
    private readonly Guid _testProductId;
    private readonly Guid _testProductUrlId;

    public RecordPriceHistoryHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _testUserId = Guid.NewGuid();
        _testProductId = Guid.NewGuid();
        _testProductUrlId = Guid.NewGuid();

        var user = new User
        {
            Id = _testUserId,
            Email = "test@example.com",
            Name = "Test",
            PasswordHash = "hash"
        };
        _dbContext.Users.Add(user);

        var product = new Product
        {
            Id = _testProductId,
            UserId = _testUserId,
            Name = "Test Product",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(product);

        var productUrl = new ProductUrl
        {
            Id = _testProductUrlId,
            ProductId = _testProductId,
            Url = "https://example.com/product",
            StoreId = "test-store"
        };
        _dbContext.ProductUrls.Add(productUrl);
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task HandleAsync_FirstPriceRecord_CreatesPricePoint()
    {
        var @event = new PriceUpdatedEvent(_testProductId, null, 99.99m, "USD", _testProductUrlId);

        await RecordPriceHistoryHandler.HandleAsync(
            @event, _dbContext, TimeProvider.System, NullLogger.Instance, TestContext.Current.CancellationToken);

        var points = await _dbContext.PricePoints.ToListAsync(TestContext.Current.CancellationToken);
        points.Should().HaveCount(1);
        points[0].Price.Should().Be(99.99m);
        points[0].Currency.Should().Be("USD");
        points[0].ProductId.Should().Be(_testProductId);
        points[0].ProductUrlId.Should().Be(_testProductUrlId);
    }

    [Fact]
    public async Task HandleAsync_PriceUnchanged_SkipsRecording()
    {
        // Seed an existing price point
        _dbContext.PricePoints.Add(new PricePoint
        {
            Id = Guid.NewGuid(),
            ProductId = _testProductId,
            ProductUrlId = _testProductUrlId,
            Price = 99.99m,
            Currency = "USD",
            RecordedAt = DateTime.UtcNow.AddHours(-1)
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(_testProductId, 99.99m, 99.99m, "USD", _testProductUrlId);

        await RecordPriceHistoryHandler.HandleAsync(
            @event, _dbContext, TimeProvider.System, NullLogger.Instance, TestContext.Current.CancellationToken);

        var points = await _dbContext.PricePoints.ToListAsync(TestContext.Current.CancellationToken);
        points.Should().HaveCount(1); // No new point added
    }

    [Fact]
    public async Task HandleAsync_PriceChanged_CreatesNewPricePoint()
    {
        // Seed an existing price point
        _dbContext.PricePoints.Add(new PricePoint
        {
            Id = Guid.NewGuid(),
            ProductId = _testProductId,
            ProductUrlId = _testProductUrlId,
            Price = 99.99m,
            Currency = "USD",
            RecordedAt = DateTime.UtcNow.AddHours(-1)
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(_testProductId, 99.99m, 79.99m, "USD", _testProductUrlId);

        await RecordPriceHistoryHandler.HandleAsync(
            @event, _dbContext, TimeProvider.System, NullLogger.Instance, TestContext.Current.CancellationToken);

        var points = await _dbContext.PricePoints
            .OrderBy(p => p.RecordedAt)
            .ToListAsync(TestContext.Current.CancellationToken);
        points.Should().HaveCount(2);
        points[1].Price.Should().Be(79.99m);
    }

    [Fact]
    public async Task HandleAsync_MultipleUrls_DeduplicatesPerUrl()
    {
        // Create a second URL
        var secondUrlId = Guid.NewGuid();
        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = secondUrlId,
            ProductId = _testProductId,
            Url = "https://other.com/product",
            StoreId = "other-store"
        });

        // Seed price point only for URL1
        _dbContext.PricePoints.Add(new PricePoint
        {
            Id = Guid.NewGuid(),
            ProductId = _testProductId,
            ProductUrlId = _testProductUrlId,
            Price = 99.99m,
            Currency = "USD",
            RecordedAt = DateTime.UtcNow.AddHours(-1)
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Same price as URL1 — but this is for URL2 which has no history, so should record
        var @event = new PriceUpdatedEvent(_testProductId, null, 99.99m, "USD", secondUrlId);

        await RecordPriceHistoryHandler.HandleAsync(
            @event, _dbContext, TimeProvider.System, NullLogger.Instance, TestContext.Current.CancellationToken);

        var points = await _dbContext.PricePoints.ToListAsync(TestContext.Current.CancellationToken);
        points.Should().HaveCount(2); // Original + new for URL2
        points.Should().Contain(p => p.ProductUrlId == secondUrlId);
    }

    [Fact]
    public async Task HandleAsync_MultiUrl_RecordsScrapedUrlPrice_NotProductMin()
    {
        // Multi-URL case: another URL is the product MIN, so event.NewPrice (product-level)
        // differs from event.UrlPrice (the URL that was actually scraped).
        // History must log the URL-level value — otherwise every per-URL chart shows the MIN.
        var @event = new PriceUpdatedEvent(
            ProductId: _testProductId,
            OldPrice: 50m,
            NewPrice: 50m,           // product MIN unchanged (other URL is cheaper)
            Currency: "USD",
            ProductUrlId: _testProductUrlId,
            UrlPrice: 90m,           // this URL just scraped $90
            UrlCurrency: "USD");

        await RecordPriceHistoryHandler.HandleAsync(
            @event, _dbContext, TimeProvider.System, NullLogger.Instance, TestContext.Current.CancellationToken);

        var points = await _dbContext.PricePoints.ToListAsync(TestContext.Current.CancellationToken);
        points.Should().HaveCount(1);
        points[0].Price.Should().Be(90m);
        points[0].ProductUrlId.Should().Be(_testProductUrlId);
    }

    [Fact]
    public async Task HandleAsync_RecordsPricePoint_WithCorrectCurrency()
    {
        var @event = new PriceUpdatedEvent(_testProductId, null, 150.00m, "EUR", _testProductUrlId);

        await RecordPriceHistoryHandler.HandleAsync(
            @event, _dbContext, TimeProvider.System, NullLogger.Instance, TestContext.Current.CancellationToken);

        var point = await _dbContext.PricePoints.SingleAsync(TestContext.Current.CancellationToken);
        point.Currency.Should().Be("EUR");
        point.Price.Should().Be(150.00m);
    }

    [Fact]
    public async Task HandleAsync_WithNullProductUrlId_CreatesPricePoint()
    {
        // Arrange — event with null ProductUrlId (global price event, not URL-specific)
        var @event = new PriceUpdatedEvent(_testProductId, null, 99.99m, "USD", null);

        // Act
        await RecordPriceHistoryHandler.HandleAsync(
            @event, _dbContext, TimeProvider.System, NullLogger.Instance, TestContext.Current.CancellationToken);

        // Assert
        var point = await _dbContext.PricePoints.SingleAsync(TestContext.Current.CancellationToken);
        point.ProductId.Should().Be(_testProductId);
        point.ProductUrlId.Should().BeNull();
        point.Price.Should().Be(99.99m);
    }

    [Fact]
    public async Task HandleAsync_RecordedAt_IsCloseToUtcNow()
    {
        // Arrange
        var beforeCall = DateTime.UtcNow;
        var @event = new PriceUpdatedEvent(_testProductId, null, 99.99m, "USD", _testProductUrlId);

        // Act
        await RecordPriceHistoryHandler.HandleAsync(
            @event, _dbContext, TimeProvider.System, NullLogger.Instance, TestContext.Current.CancellationToken);
        var afterCall = DateTime.UtcNow;

        // Assert
        var point = await _dbContext.PricePoints.SingleAsync(TestContext.Current.CancellationToken);
        point.RecordedAt.Should().BeOnOrAfter(beforeCall);
        point.RecordedAt.Should().BeOnOrBefore(afterCall.AddSeconds(1));
    }

    [Fact]
    public async Task HandleAsync_MultiplePriceChanges_BuildsHistoryChain()
    {
        // Arrange & Act — simulate 3 consecutive price updates
        var prices = new[] { 100m, 90m, 80m };

        foreach (var price in prices)
        {
            var @event = new PriceUpdatedEvent(_testProductId, null, price, "USD", _testProductUrlId);
            await RecordPriceHistoryHandler.HandleAsync(
                @event, _dbContext, TimeProvider.System, NullLogger.Instance, TestContext.Current.CancellationToken);
        }

        // Assert — all 3 prices recorded
        var points = await _dbContext.PricePoints
            .OrderBy(p => p.RecordedAt)
            .ToListAsync(TestContext.Current.CancellationToken);

        points.Should().HaveCount(3);
        points[0].Price.Should().Be(100m);
        points[1].Price.Should().Be(90m);
        points[2].Price.Should().Be(80m);

        // Verify chronological order
        points[0].RecordedAt.Should().BeBefore(points[1].RecordedAt);
        points[1].RecordedAt.Should().BeBefore(points[2].RecordedAt);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
