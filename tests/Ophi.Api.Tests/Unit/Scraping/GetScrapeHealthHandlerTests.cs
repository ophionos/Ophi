using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Api.Features.Scraping;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Scraping;

public class GetScrapeHealthHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly GetScrapeHealth.Handler _handler;
    private readonly Guid _testUserId;

    public GetScrapeHealthHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new GetScrapeHealth.Handler(_dbContext, TimeProvider.System, NullLogger<GetScrapeHealth.Handler>.Instance);
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

    private Product CreateProduct(string name = "Test Product")
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = name,
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(product);
        return product;
    }

    private void AddScrapeLog(Guid productId, string domain, bool success, int durationMs = 100,
        string? error = null, DateTime? createdAt = null)
    {
        _dbContext.ScrapeLogs.Add(new ScrapeLog
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Success = success,
            DurationMs = durationMs,
            Error = error,
            StoreDomain = domain,
            CreatedAt = createdAt ?? DateTime.UtcNow
        });
    }

    [Fact]
    public async Task Handle_NoProducts_ReturnsNullOverallSuccessRate()
    {
        var query = new GetScrapeHealth.Query { UserId = _testUserId };

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Domains.Should().BeEmpty();
        result.Summary.TotalDomains.Should().Be(0);
        // No scrapes have run, so there is no rate to report. Reporting 1.0 here rendered a
        // confident green "100%" on a brand-new account that had never scraped anything.
        result.Summary.OverallSuccessRate.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ProductsButNoScrapeLogs_ReturnsNullOverallSuccessRate()
    {
        CreateProduct();
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetScrapeHealth.Query { UserId = _testUserId };

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Domains.Should().BeEmpty();
        result.Summary.TotalScrapes7d.Should().Be(0);
        result.Summary.OverallSuccessRate.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AllScrapesOutsideWindow_ReturnsNullOverallSuccessRate()
    {
        var product = CreateProduct();
        AddScrapeLog(product.Id, "amazon.com", true, 100);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Backdate past the 7-day window (SaveChangesAsync overrides CreatedAt on Add)
        var log = _dbContext.ScrapeLogs.First();
        log.CreatedAt = DateTime.UtcNow.AddDays(-8);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetScrapeHealth.Query { UserId = _testUserId };

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Summary.TotalScrapes7d.Should().Be(0);
        result.Summary.OverallSuccessRate.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AllScrapesFailed_ReturnsZeroOverallSuccessRate()
    {
        var product = CreateProduct();
        AddScrapeLog(product.Id, "amazon.com", false, 50, "boom");
        AddScrapeLog(product.Id, "amazon.com", false, 50, "boom");
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetScrapeHealth.Query { UserId = _testUserId };

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // A genuine 0% is real data, not "no data" — it must stay 0.0 and never collapse to null.
        result.Summary.TotalScrapes7d.Should().Be(2);
        result.Summary.OverallSuccessRate.Should().Be(0.0);
    }

    [Fact]
    public async Task Handle_SingleDomain_AllSuccessful_ReturnsHealthy()
    {
        var product = CreateProduct();
        AddScrapeLog(product.Id, "amazon.com", true, 200);
        AddScrapeLog(product.Id, "amazon.com", true, 300);
        AddScrapeLog(product.Id, "amazon.com", true, 150);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetScrapeHealth.Query { UserId = _testUserId };

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Domains.Should().HaveCount(1);
        var domain = result.Domains[0];
        domain.Domain.Should().Be("amazon.com");
        domain.TotalScrapes.Should().Be(3);
        domain.SuccessCount.Should().Be(3);
        domain.FailureCount.Should().Be(0);
        domain.SuccessRate.Should().Be(1.0);
        domain.LastFailureMessage.Should().BeNull();
        result.Summary.DomainsHealthy.Should().Be(1);
        result.Summary.DomainsUnhealthy.Should().Be(0);
        // A real perfect score must still report 1.0 — the no-data fix must not hide it.
        result.Summary.TotalScrapes7d.Should().Be(3);
        result.Summary.OverallSuccessRate.Should().Be(1.0);
    }

    [Fact]
    public async Task Handle_MultipleDomains_GroupedCorrectly()
    {
        var product = CreateProduct();
        AddScrapeLog(product.Id, "amazon.com", true, 100);
        AddScrapeLog(product.Id, "amazon.com", true, 200);
        AddScrapeLog(product.Id, "ebay.com", true, 150);
        AddScrapeLog(product.Id, "ebay.com", false, 50, "Parse error");
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetScrapeHealth.Query { UserId = _testUserId };

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Domains.Should().HaveCount(2);
        result.Summary.TotalDomains.Should().Be(2);
        result.Summary.TotalScrapes7d.Should().Be(4);

        var amazon = result.Domains.First(d => d.Domain == "amazon.com");
        amazon.SuccessRate.Should().Be(1.0);

        var ebay = result.Domains.First(d => d.Domain == "ebay.com");
        ebay.SuccessRate.Should().Be(0.5);
        ebay.LastFailureMessage.Should().Be("Parse error");
    }

    [Fact]
    public async Task Handle_OldLogsExcluded_Only7Days()
    {
        var product = CreateProduct();
        AddScrapeLog(product.Id, "amazon.com", true, 100);
        AddScrapeLog(product.Id, "amazon.com", false, 50, "old error");
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Backdate the old log after save (SaveChangesAsync overrides CreatedAt on Add)
        var oldLog = _dbContext.ScrapeLogs.First(s => !s.Success);
        oldLog.CreatedAt = DateTime.UtcNow.AddDays(-8);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetScrapeHealth.Query { UserId = _testUserId };

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Domains.Should().HaveCount(1);
        var domain = result.Domains[0];
        domain.TotalScrapes.Should().Be(1);
        domain.SuccessRate.Should().Be(1.0);
    }

    [Fact]
    public async Task Handle_OtherUsersLogsExcluded()
    {
        var otherUserId = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = otherUserId,
            Email = "other@example.com",
            Name = "Other",
            PasswordHash = "hash"
        });
        var otherProduct = new Product
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Other Product",
            Currency = "USD",
            Status = ProductStatus.Active
        };
        _dbContext.Products.Add(otherProduct);
        AddScrapeLog(otherProduct.Id, "amazon.com", true, 100);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetScrapeHealth.Query { UserId = _testUserId };

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Domains.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DurationPercentiles_CalculatedCorrectly()
    {
        var product = CreateProduct();
        // Add 10 logs with durations 100, 200, 300, ..., 1000
        for (int i = 1; i <= 10; i++)
        {
            AddScrapeLog(product.Id, "amazon.com", true, i * 100);
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetScrapeHealth.Query { UserId = _testUserId };

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        var domain = result.Domains[0];
        domain.P50DurationMs.Should().BeApproximately(550, 1); // median of 100..1000
        domain.P95DurationMs.Should().BeGreaterThan(900);
    }

    [Fact]
    public async Task Handle_SummaryCategories_CorrectThresholds()
    {
        var product = CreateProduct();

        // Healthy domain (100% success)
        for (int i = 0; i < 10; i++)
            AddScrapeLog(product.Id, "healthy.com", true, 100);

        // Degraded domain (85% success = 0.85 -> between 0.80 and 0.95)
        for (int i = 0; i < 17; i++)
            AddScrapeLog(product.Id, "degraded.com", true, 100);
        for (int i = 0; i < 3; i++)
            AddScrapeLog(product.Id, "degraded.com", false, 50, "error");

        // Unhealthy domain (50% success = below 0.80)
        for (int i = 0; i < 5; i++)
            AddScrapeLog(product.Id, "unhealthy.com", true, 100);
        for (int i = 0; i < 5; i++)
            AddScrapeLog(product.Id, "unhealthy.com", false, 50, "error");

        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetScrapeHealth.Query { UserId = _testUserId };

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Summary.DomainsHealthy.Should().Be(1);
        result.Summary.DomainsDegraded.Should().Be(1);
        result.Summary.DomainsUnhealthy.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ScrapesToday_CountedCorrectly()
    {
        var product = CreateProduct();
        AddScrapeLog(product.Id, "amazon.com", true, 100); // today
        AddScrapeLog(product.Id, "amazon.com", true, 100); // will backdate to yesterday
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Backdate second log to yesterday after save
        var logs = _dbContext.ScrapeLogs.OrderBy(s => s.Id).ToList();
        logs[1].CreatedAt = DateTime.UtcNow.AddDays(-1);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetScrapeHealth.Query { UserId = _testUserId };

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Domains[0].ScrapesToday.Should().Be(1);
        result.Domains[0].TotalScrapes.Should().Be(2);
    }

    [Fact]
    public async Task Handle_OrderedByTotalScrapesDescending()
    {
        var product = CreateProduct();
        AddScrapeLog(product.Id, "few.com", true, 100);
        for (int i = 0; i < 5; i++)
            AddScrapeLog(product.Id, "many.com", true, 100);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetScrapeHealth.Query { UserId = _testUserId };

        var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        result.Domains[0].Domain.Should().Be("many.com");
        result.Domains[1].Domain.Should().Be("few.com");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
