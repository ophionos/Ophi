using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.Worker.Services;
using Ophi.Worker.Settings;

namespace Ophi.Infrastructure.Tests.Services;

public class DataRetentionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly ServiceProvider _serviceProvider;
    private readonly Guid _testUserId;

    public DataRetentionServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<OphiDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new OphiDbContext(options);
        _dbContext.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddDbContext<OphiDbContext>(opt => opt.UseSqlite(_connection));
        _serviceProvider = services.BuildServiceProvider();

        _testUserId = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = _testUserId,
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hash"
        });
        _dbContext.SaveChanges();
    }

    private DataRetentionService CreateService(int retentionDays = 30)
    {
        var settings = Options.Create(new WorkerSettings { ScrapeLogRetentionDays = retentionDays });
        var logger = new Mock<ILogger<DataRetentionService>>();
        return new DataRetentionService(_serviceProvider, settings, TimeProvider.System, logger.Object);
    }

    private (Product product, ProductUrl productUrl) CreateProduct()
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "Test Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        var productUrl = new ProductUrl
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Url = "https://example.com/product",
            Currency = "USD"
        };
        return (product, productUrl);
    }

    private async Task AddScrapeLogWithDate(Guid productId, Guid productUrlId, decimal price, DateTime createdAt)
    {
        var log = new ScrapeLog
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ProductUrlId = productUrlId,
            Success = true,
            Price = price,
            DurationMs = 100
        };
        _dbContext.ScrapeLogs.Add(log);
        await _dbContext.SaveChangesAsync();

        // Override CreatedAt via raw SQL since SaveChangesAsync sets it to UtcNow
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE ScrapeLogs SET CreatedAt = {0} WHERE Id = {1}",
            createdAt, log.Id);
    }

    [Fact]
    public async Task CleanupIfDueAsync_DeletesOldLogs()
    {
        // Arrange
        var (product, productUrl) = CreateProduct();
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await AddScrapeLogWithDate(product.Id, productUrl.Id, 10m, DateTime.UtcNow.AddDays(-40));
        await AddScrapeLogWithDate(product.Id, productUrl.Id, 20m, DateTime.UtcNow.AddDays(-5));

        var service = CreateService(retentionDays: 30);

        // Act
        await service.CleanupIfDueAsync(TestContext.Current.CancellationToken);

        // Assert - need fresh context to see ExecuteDelete results
        using var scope = _serviceProvider.CreateScope();
        var freshDb = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        var remaining = await freshDb.ScrapeLogs.ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        remaining.Should().HaveCount(1);
        remaining[0].Price.Should().Be(20m);
    }

    [Fact]
    public async Task CleanupIfDueAsync_KeepsRecentLogs()
    {
        // Arrange
        var (product, productUrl) = CreateProduct();
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await AddScrapeLogWithDate(product.Id, productUrl.Id, 10m, DateTime.UtcNow.AddDays(-10));

        var service = CreateService(retentionDays: 30);

        // Act
        await service.CleanupIfDueAsync(TestContext.Current.CancellationToken);

        // Assert
        using var scope = _serviceProvider.CreateScope();
        var freshDb = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        var remaining = await freshDb.ScrapeLogs.ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        remaining.Should().HaveCount(1);
    }

    [Fact]
    public async Task CleanupIfDueAsync_SkipsIfLessThan24hSinceLastCleanup()
    {
        // Arrange
        var (product, productUrl) = CreateProduct();
        _dbContext.Products.Add(product);
        _dbContext.ProductUrls.Add(productUrl);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await AddScrapeLogWithDate(product.Id, productUrl.Id, 10m, DateTime.UtcNow.AddDays(-40));

        var service = CreateService(retentionDays: 30);

        // First cleanup should run
        await service.CleanupIfDueAsync(TestContext.Current.CancellationToken);

        using (var scope1 = _serviceProvider.CreateScope())
        {
            var db1 = scope1.ServiceProvider.GetRequiredService<OphiDbContext>();
            var afterFirst = await db1.ScrapeLogs.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
            afterFirst.Should().Be(0);
        }

        // Add another old log
        await AddScrapeLogWithDate(product.Id, productUrl.Id, 15m, DateTime.UtcNow.AddDays(-40));

        // Act - Second cleanup should be skipped (cooldown)
        await service.CleanupIfDueAsync(TestContext.Current.CancellationToken);

        // Assert - old log should still exist
        using var scope2 = _serviceProvider.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<OphiDbContext>();
        var afterSecond = await db2.ScrapeLogs.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        afterSecond.Should().Be(1);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
