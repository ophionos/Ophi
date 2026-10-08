using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Tests.Helpers;
using Ophi.TestHelpers;
using Ophi.Worker.Services;

namespace Ophi.Infrastructure.Tests.Services;

public class PriceCheckDispatcherQueryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly Guid _userId;
    private readonly Guid _productId;
    private readonly Guid _productUrlId;

    public PriceCheckDispatcherQueryTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _userId = Guid.NewGuid();
        _productId = Guid.NewGuid();
        _productUrlId = Guid.NewGuid();

        _dbContext.Users.Add(new User
        {
            Id = _userId,
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hash"
        });

        _dbContext.Products.Add(new Product
        {
            Id = _productId,
            Name = "Test Product",
            UserId = _userId,
            Status = ProductStatus.Active
        });

        _dbContext.ProductUrls.Add(new ProductUrl
        {
            Id = _productUrlId,
            Url = "https://example.com/product",
            ProductId = _productId,
            Status = ProductUrlStatus.Active
        });

        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task BuildDueProductUrlQuery_WithNoCacheTtl_DispatchesDueUrl()
    {
        var url = _dbContext.ProductUrls.First(pu => pu.Id == _productUrlId);
        url.MarkChecked(DateTime.UtcNow.AddMinutes(-120)); // 2 hours ago, well past default 60m interval
        _dbContext.SaveChanges();

        var now = DateTime.UtcNow;
        var result = await PriceCheckDispatcher.SelectDueProductUrlIdsAsync(_dbContext.ProductUrls, now, 100, TestContext.Current.CancellationToken);

        result.Should().Contain(_productUrlId);
    }

    [Fact]
    public async Task BuildDueProductUrlQuery_WithNullLastCheckedAt_IncludesUrl()
    {
        // LastCheckedAt is null (never scraped) — should always be included regardless of cache TTL
        var user = _dbContext.Users.First(u => u.Id == _userId);
        user.ScrapeCacheTtlMinutes = 60;
        _dbContext.SaveChanges();

        var now = DateTime.UtcNow;
        var result = await PriceCheckDispatcher.SelectDueProductUrlIdsAsync(_dbContext.ProductUrls, now, 100, TestContext.Current.CancellationToken);

        result.Should().Contain(_productUrlId);
    }

    [Fact]
    public async Task BuildDueProductUrlQuery_WithCacheTtl_SkipsRecentlyScrapedUrl()
    {
        var user = _dbContext.Users.First(u => u.Id == _userId);
        user.ScrapeCacheTtlMinutes = 60;

        var product = _dbContext.Products.First(p => p.Id == _productId);
        product.CheckIntervalMinutes = 15; // Due every 15 min

        var url = _dbContext.ProductUrls.First(pu => pu.Id == _productUrlId);
        url.MarkChecked(DateTime.UtcNow.AddMinutes(-30)); // 30 min ago — past 15m interval, but within 60m cache TTL

        _dbContext.SaveChanges();

        var now = DateTime.UtcNow;
        var result = await PriceCheckDispatcher.SelectDueProductUrlIdsAsync(_dbContext.ProductUrls, now, 100, TestContext.Current.CancellationToken);

        result.Should().NotContain(_productUrlId);
    }

    [Fact]
    public async Task BuildDueProductUrlQuery_WithExpiredCacheTtl_IncludesUrl()
    {
        var user = _dbContext.Users.First(u => u.Id == _userId);
        user.ScrapeCacheTtlMinutes = 60;

        var product = _dbContext.Products.First(p => p.Id == _productId);
        product.CheckIntervalMinutes = 15;

        var url = _dbContext.ProductUrls.First(pu => pu.Id == _productUrlId);
        url.MarkChecked(DateTime.UtcNow.AddMinutes(-90)); // 90 min ago — past both 15m interval and 60m cache TTL

        _dbContext.SaveChanges();

        var now = DateTime.UtcNow;
        var result = await PriceCheckDispatcher.SelectDueProductUrlIdsAsync(_dbContext.ProductUrls, now, 100, TestContext.Current.CancellationToken);

        result.Should().Contain(_productUrlId);
    }

    [Fact]
    public async Task BuildDueProductUrlQuery_WithNoCacheTtlSet_DoesNotFilterByCacheTtl()
    {
        // User has no cache TTL — URL should be dispatched based on check interval only
        var product = _dbContext.Products.First(p => p.Id == _productId);
        product.CheckIntervalMinutes = 15;

        var url = _dbContext.ProductUrls.First(pu => pu.Id == _productUrlId);
        url.MarkChecked(DateTime.UtcNow.AddMinutes(-20)); // 20 min ago — past 15m interval, no cache TTL

        _dbContext.SaveChanges();

        var now = DateTime.UtcNow;
        var result = await PriceCheckDispatcher.SelectDueProductUrlIdsAsync(_dbContext.ProductUrls, now, 100, TestContext.Current.CancellationToken);

        result.Should().Contain(_productUrlId);
    }

    [Fact]
    public async Task SelectDueProductUrls_RespectsPerProductOverride_LongerInterval()
    {
        // Product has a 4-hour interval; URL was last checked 1 hour ago.
        // Two-stage refactor must NOT dispatch this URL (passes stage-1 stale-floor of 15min
        // but fails stage-2 cascade against the per-product 240-min interval).
        var product = _dbContext.Products.First(p => p.Id == _productId);
        product.CheckIntervalMinutes = 240;

        var url = _dbContext.ProductUrls.First(pu => pu.Id == _productUrlId);
        url.MarkChecked(DateTime.UtcNow.AddMinutes(-60));

        _dbContext.SaveChanges();

        var now = DateTime.UtcNow;
        var result = await PriceCheckDispatcher.SelectDueProductUrlIdsAsync(_dbContext.ProductUrls, now, 100, TestContext.Current.CancellationToken);

        result.Should().NotContain(_productUrlId);
    }

    [Fact]
    public async Task SelectDueProductUrls_RespectsPerProductOverride_ShorterInterval()
    {
        // Product has a 15-min interval; URL was last checked 30 minutes ago.
        // Cascade resolves to 15min, so the URL is due despite the user default being 60min.
        var user = _dbContext.Users.First(u => u.Id == _userId);
        user.DefaultCheckIntervalMinutes = 60;

        var product = _dbContext.Products.First(p => p.Id == _productId);
        product.CheckIntervalMinutes = 15;

        var url = _dbContext.ProductUrls.First(pu => pu.Id == _productUrlId);
        url.MarkChecked(DateTime.UtcNow.AddMinutes(-30));

        _dbContext.SaveChanges();

        var now = DateTime.UtcNow;
        var result = await PriceCheckDispatcher.SelectDueProductUrlIdsAsync(_dbContext.ProductUrls, now, 100, TestContext.Current.CancellationToken);

        result.Should().Contain(_productUrlId);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
