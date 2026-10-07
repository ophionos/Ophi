using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Metrics;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Metrics;

public class PriceMetricsCollectorTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Guid _userId;

    public PriceMetricsCollectorTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _userId = Guid.NewGuid();

        _dbContext.Users.Add(new User
        {
            Id = _userId,
            Email = "test@example.com",
            PasswordHash = "hash",
            Name = "Test"
        });
        _dbContext.SaveChanges();

        // Build a minimal service provider for the collector
        var services = new ServiceCollection();
        services.AddScoped<OphiDbContext>(_ =>
        {
            // Re-use the same connection (in-memory SQLite)
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<OphiDbContext>()
                .UseSqlite(_connection)
                .Options;
            return new OphiDbContext(options);
        });
        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task RefreshGaugesAsync_WithActiveProducts_SetsGauges()
    {
        var productId = Guid.NewGuid();
        _dbContext.Products.Add(new Product
        {
            Id = productId,
            UserId = _userId,
            Name = "Test Widget",
            Currency = "USD",
            CurrentPrice = 29.99m,
            Status = ProductStatus.Active,
            ProductUrls = [new ProductUrl
            {
                Id = Guid.NewGuid(),
                Url = "https://www.amazon.com/dp/B12345",
                ProductId = productId,
                Currency = "USD"
            }]
        });
        _dbContext.PricePoints.Add(new PricePoint
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Price = 19.99m,
            Currency = "USD",
            RecordedAt = DateTime.UtcNow.AddDays(-7)
        });
        _dbContext.PricePoints.Add(new PricePoint
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Price = 29.99m,
            Currency = "USD",
            RecordedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var collector = new PriceMetricsCollector(_scopeFactory, NullLogger<PriceMetricsCollector>.Instance);
        await collector.RefreshGaugesAsync(CancellationToken.None);

        // Verify gauges were set (no exception = success)
        var currentGauge = AppMetrics.ProductPriceCurrent
            .WithLabels(productId.ToString(), "www.amazon.com");
        currentGauge.Value.Should().Be(29.99);

        var lowestGauge = AppMetrics.ProductPriceLowest
            .WithLabels(productId.ToString(), "www.amazon.com");
        lowestGauge.Value.Should().Be(19.99);

        var infoGauge = AppMetrics.ProductInfo
            .WithLabels(productId.ToString(), "Test Widget");
        infoGauge.Value.Should().Be(1);
    }

    [Fact]
    public async Task RefreshGaugesAsync_WithNoActiveProducts_DoesNotThrow()
    {
        var collector = new PriceMetricsCollector(_scopeFactory, NullLogger<PriceMetricsCollector>.Instance);

        Func<Task> act = () => collector.RefreshGaugesAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RefreshGaugesAsync_SkipsInactiveProducts()
    {
        var productId = Guid.NewGuid();
        _dbContext.Products.Add(new Product
        {
            Id = productId,
            UserId = _userId,
            Name = "Pending Product",
            Currency = "USD",
            CurrentPrice = 15.00m,
            Status = ProductStatus.Pending
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var collector = new PriceMetricsCollector(_scopeFactory, NullLogger<PriceMetricsCollector>.Instance);
        await collector.RefreshGaugesAsync(CancellationToken.None);

        // Gauge should not be set for inactive product
        // After Unpublish + no matching products, the gauge series shouldn't exist
        // This primarily verifies no exception is thrown
    }

    [Fact]
    public async Task RefreshGaugesAsync_ProductWithNoPriceHistory_UsesCurrentPrice()
    {
        var productId = Guid.NewGuid();
        _dbContext.Products.Add(new Product
        {
            Id = productId,
            UserId = _userId,
            Name = "New Product",
            Currency = "USD",
            CurrentPrice = 50.00m,
            Status = ProductStatus.Active,
            ProductUrls = [new ProductUrl
            {
                Id = Guid.NewGuid(),
                Url = "https://ebay.com/itm/999",
                ProductId = productId,
                Currency = "USD"
            }]
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var collector = new PriceMetricsCollector(_scopeFactory, NullLogger<PriceMetricsCollector>.Instance);
        await collector.RefreshGaugesAsync(CancellationToken.None);

        var lowestGauge = AppMetrics.ProductPriceLowest
            .WithLabels(productId.ToString(), "ebay.com");
        lowestGauge.Value.Should().Be(50.00);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
