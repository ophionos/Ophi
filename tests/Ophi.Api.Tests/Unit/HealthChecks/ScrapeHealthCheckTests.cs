using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ophi.Api.Common.HealthChecks;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;

namespace Ophi.Api.Tests.Unit.HealthChecks;

public class ScrapeHealthCheckTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly Infrastructure.Persistence.OphiDbContext _dbContext;
    private readonly Guid _userId;
    private readonly Guid _productId;

    public ScrapeHealthCheckTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _userId = Guid.NewGuid();
        _productId = Guid.NewGuid();

        // Seed required parent entities
        _dbContext.Users.Add(new User
        {
            Id = _userId,
            Email = "test@example.com",
            PasswordHash = "hash",
            Name = "Test"
        });
        _dbContext.Products.Add(new Product
        {
            Id = _productId,
            UserId = _userId,
            Name = "Test Product",
            Currency = "USD",
            Status = ProductStatus.Active
        });
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task CheckHealthAsync_NoRecentScrapes_ReturnsHealthy()
    {
        var check = new ScrapeHealthCheck(_dbContext, TimeProvider.System);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["totalScrapes"].Should().Be(0);
    }

    [Fact]
    public async Task CheckHealthAsync_AllSuccessful_ReturnsHealthy()
    {
        SeedScrapeLogs(success: 10, failure: 0);
        var check = new ScrapeHealthCheck(_dbContext, TimeProvider.System);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
        ((double)result.Data["successRate"]).Should().Be(1.0);
        result.Data["totalScrapes"].Should().Be(10);
    }

    [Fact]
    public async Task CheckHealthAsync_SuccessRateAboveThreshold_ReturnsHealthy()
    {
        SeedScrapeLogs(success: 9, failure: 1); // 90%
        var check = new ScrapeHealthCheck(_dbContext, TimeProvider.System);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
        ((double)result.Data["successRate"]).Should().Be(0.9);
    }

    [Fact]
    public async Task CheckHealthAsync_SuccessRateAtThreshold_ReturnsHealthy()
    {
        SeedScrapeLogs(success: 8, failure: 2); // 80% exactly
        var check = new ScrapeHealthCheck(_dbContext, TimeProvider.System);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_SuccessRateBelowThreshold_ReturnsDegraded()
    {
        SeedScrapeLogs(success: 7, failure: 3); // 70%
        var check = new ScrapeHealthCheck(_dbContext, TimeProvider.System);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("below threshold");
        ((int)result.Data["failureCount"]).Should().Be(3);
    }

    [Fact]
    public async Task CheckHealthAsync_AllFailed_ReturnsDegraded()
    {
        SeedScrapeLogs(success: 0, failure: 5);
        var check = new ScrapeHealthCheck(_dbContext, TimeProvider.System);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Degraded);
        ((double)result.Data["successRate"]).Should().Be(0.0);
    }

    [Fact]
    public async Task CheckHealthAsync_IgnoresOldScrapes()
    {
        // Add old scrape logs (older than 1 hour) — should be ignored
        var oldTime = DateTime.UtcNow.AddHours(-2);
        for (var i = 0; i < 5; i++)
        {
            _dbContext.ScrapeLogs.Add(new ScrapeLog
            {
                Id = Guid.NewGuid(),
                ProductId = _productId,
                Success = false,
                DurationMs = 100,
                CreatedAt = oldTime
            });
        }

        // Add recent successful scrapes
        SeedScrapeLogs(success: 5, failure: 0);
        var check = new ScrapeHealthCheck(_dbContext, TimeProvider.System);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["totalScrapes"].Should().Be(5); // Only recent ones
    }

    private void SeedScrapeLogs(int success, int failure)
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < success; i++)
        {
            _dbContext.ScrapeLogs.Add(new ScrapeLog
            {
                Id = Guid.NewGuid(),
                ProductId = _productId,
                Success = true,
                Price = 9.99m,
                DurationMs = 200,
                CreatedAt = now.AddMinutes(-i)
            });
        }

        for (var i = 0; i < failure; i++)
        {
            _dbContext.ScrapeLogs.Add(new ScrapeLog
            {
                Id = Guid.NewGuid(),
                ProductId = _productId,
                Success = false,
                Error = "Extraction failed",
                DurationMs = 500,
                CreatedAt = now.AddMinutes(-success - i)
            });
        }

        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
