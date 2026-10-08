using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Commands;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;
using Wolverine;

namespace Ophi.Api.Tests.Integration;

public class OphiWebApplicationFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    /// <summary>
    /// Drops and recreates the SQLite schema so callers see an empty database. Use this
    /// from a test's setup when you need isolation from sibling tests in the same class —
    /// the default is shared state via <c>IClassFixture</c>, and most tests work around
    /// that with unique GUID emails. Prefer the workaround unless you actually need a
    /// clean slate (e.g., tests asserting "zero products" against a class fixture).
    /// </summary>
    public async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
        await db.Database.EnsureDeletedAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registrations. EF Core 10 registers the options-building
            // lambda as an IDbContextOptionsConfiguration<OphiDbContext> singleton (separate from
            // DbContextOptions<>), so we must strip that too — otherwise AddInfrastructure's real
            // (Postgres) provider lambda still runs and overrides/clobbers our in-memory SQLite.
            var descriptorsToRemove = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<OphiDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(IDbContextOptionsConfiguration<OphiDbContext>) ||
                    d.ServiceType == typeof(OphiDbContext))
                .ToList();

            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            // Create and open a persistent SQLite connection for the test
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            // Add DbContext using the in-memory SQLite database
            services.AddDbContext<OphiDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            // Replace scraping service with a mock that always succeeds
            var scrapingDescriptors = services.Where(
                d => d.ServiceType == typeof(IScrapingService)).ToList();

            foreach (var desc in scrapingDescriptors)
            {
                services.Remove(desc);
            }

            services.AddScoped<IScrapingService, MockScrapingService>();

            // Configure Wolverine to discover handlers in this test assembly
            services.ConfigureOptions<WolverineOptionsConfigurator>();

            // Ensure database is created
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OphiDbContext>();
            db.Database.EnsureCreated();
        });

        builder.UseEnvironment("Testing");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection?.Dispose();
        }
        base.Dispose(disposing);
    }
}

// Configure Wolverine options to include test handlers
public class WolverineOptionsConfigurator : Microsoft.Extensions.Options.IConfigureOptions<WolverineOptions>
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.IncludeAssembly(typeof(OphiWebApplicationFactory).Assembly);
    }
}

// Mock scraping service for integration tests
public class MockScrapingService : IScrapingService
{
    public Task<ScrapingResult> ScrapeProductAsync(string url, string? customSelector = null, Guid? userId = null, bool captureHtml = false, CancellationToken cancellationToken = default)
    {
        // Return a successful scraping result for any URL
        var result = new ScrapingResult
        {
            Success = true,
            Name = "Test Product",
            Price = 99.99m,
            Currency = "USD",
            ImageUrl = "https://example.com/image.jpg"
        };
        return Task.FromResult(result);
    }

    public Task<ScrapingResult> ScrapeWithConfigAsync(string url, StoreConfig config, CancellationToken cancellationToken = default)
    {
        return ScrapeProductAsync(url, cancellationToken: cancellationToken);
    }
}

// Wolverine handler for ScrapeProductUrlCommand in tests.
// Matches the command that AddProduct, AddProductUrl, RetryScrapeProductUrl, and ImportProducts
// actually publish. Runs the scrape synchronously
// against whatever IScrapingService is wired into the test factory so failure paths can be
// exercised end-to-end with a different mock.
public static class TestScrapeProductUrlHandler
{
    public static async Task HandleAsync(ScrapeProductUrlCommand command, OphiDbContext dbContext, IScrapingService scrapingService)
    {
        var productUrl = await dbContext.ProductUrls
            .Include(pu => pu.Product)
            .FirstOrDefaultAsync(pu => pu.Id == command.ProductUrlId);
        if (productUrl == null) return;

        var product = productUrl.Product;
        if (!command.Force && product.Status != ProductStatus.Pending) return;

        var result = await scrapingService.ScrapeProductAsync(productUrl.Url, productUrl.Selector);

        if (result.Success && result.Price.HasValue)
        {
            product.Name = result.Name ?? product.Name;
            product.ImageUrl = result.ImageUrl;
            product.CurrentPrice = result.Price.Value;
            product.Currency = result.Currency ?? product.Currency;
            product.MarkActive();

            productUrl.RecordSuccessfulScrape(result.Price.Value, result.Currency, DateTime.UtcNow);

            dbContext.PricePoints.Add(new PricePoint
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Price = result.Price.Value,
                Currency = result.Currency ?? "USD",
                RecordedAt = DateTime.UtcNow
            });
        }
        else
        {
            product.MarkAsError();
            productUrl.RecordFailure(result.Error ?? "Failed to extract price", DateTime.UtcNow);
        }

        await dbContext.SaveChangesAsync();
    }
}
