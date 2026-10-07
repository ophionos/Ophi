using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Api.Tests.Integration;

/// <summary>
/// Variant of <see cref="OphiWebApplicationFactory"/> that swaps in a scraping service
/// returning a parse failure. Used to cover the scrape-failure → ProductStatus.Error pathway
/// that the default always-success mock can't exercise.
/// </summary>
public class FailingScrapeFactory : OphiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            var existing = services.Where(d => d.ServiceType == typeof(IScrapingService)).ToList();
            foreach (var d in existing) services.Remove(d);
            services.AddScoped<IScrapingService, FailingScrapingService>();
        });
    }
}

internal class FailingScrapingService : IScrapingService
{
    public Task<ScrapingResult> ScrapeProductAsync(string url, string? customSelector = null, Guid? userId = null, bool captureHtml = false, CancellationToken cancellationToken = default) =>
        Task.FromResult(ScrapingResult.Failure(
            "Could not extract price from page",
            ScrapeErrorCategory.ParseError,
            httpStatusCode: 200));

    public Task<ScrapingResult> ScrapeWithConfigAsync(string url, StoreConfig config, CancellationToken cancellationToken = default) =>
        ScrapeProductAsync(url, cancellationToken: cancellationToken);
}
