using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Infrastructure.Scraping;

/// <summary>
/// A no-op scraping service used when Playwright is disabled (DISABLE_PLAYWRIGHT).
/// Always returns a failure result indicating Playwright is unavailable.
/// </summary>
public class NoOpPlaywrightScrapingService : IScrapingService
{
    private static readonly ScrapingResult Unavailable =
        ScrapingResult.Failure("Playwright is disabled in this deployment");

    public Task<ScrapingResult> ScrapeProductAsync(string url, string? selector = null, Guid? userId = null, bool captureHtml = false, CancellationToken cancellationToken = default)
        => Task.FromResult(Unavailable);

    public Task<ScrapingResult> ScrapeWithConfigAsync(string url, StoreConfig config, CancellationToken cancellationToken = default)
        => Task.FromResult(Unavailable);
}
