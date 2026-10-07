using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Infrastructure.Scraping;

public interface IScrapingService
{
    /// <param name="captureHtml">
    /// When true, the generic adapter path also returns the page's rendered HTML in
    /// <see cref="ScrapingResult.FetchedHtml"/> so the worker can run one-shot store-config inference.
    /// Only the initial new-product scrape sets this; recurring scrapes leave it false to avoid the
    /// extra <c>page.ContentAsync()</c> round-trip on the Playwright path.
    /// </param>
    Task<ScrapingResult> ScrapeProductAsync(string url, string? selector = null, Guid? userId = null, bool captureHtml = false, CancellationToken cancellationToken = default);
    Task<ScrapingResult> ScrapeWithConfigAsync(string url, StoreConfig config, CancellationToken cancellationToken = default);
}

public record ScrapingResult
{
    public bool Success { get; init; }
    public string? Name { get; init; }
    public string? ImageUrl { get; init; }
    public decimal? Price { get; init; }
    public string? Currency { get; init; }
    public string? Error { get; init; }
    public string? DetectedSelector { get; init; }

    /// <summary>
    /// Identifies which store adapter was used (e.g., "amazon", "ebay", "generic").
    /// </summary>
    public string? StoreId { get; init; }

    /// <summary>
    /// The raw HTML fetched from the page. Populated by the HTTP <see cref="ScrapingService"/>
    /// only when the scrape used the generic adapter (no store-specific config matched), so the
    /// worker can feed it to <see cref="IAutoCreateStoreService"/> for one-shot store-config
    /// inference.
    ///
    /// Intentionally <c>null</c> from <see cref="PlaywrightScrapingService"/>: returning it would
    /// require an extra <c>page.ContentAsync()</c> round-trip per scrape, and auto-store creation
    /// already runs from the HTTP fallback path that produced the page Playwright is recovering.
    /// </summary>
    public string? FetchedHtml { get; init; }

    /// <summary>
    /// The URL after all redirects. Used for URL health monitoring to detect suspicious redirects.
    /// </summary>
    public string? FinalUrl { get; init; }

    /// <summary>
    /// HTTP status code returned by the server (null when unknown, e.g. Playwright navigation errors).
    /// </summary>
    public int? HttpStatusCode { get; init; }

    /// <summary>
    /// Structured error classification for differentiated handling in worker handlers.
    /// </summary>
    public ScrapeErrorCategory ErrorCategory { get; init; } = ScrapeErrorCategory.None;

    /// <summary>
    /// Whether the product was detected as out of stock on the page.
    /// A scrape can be Success=true with IsOutOfStock=true when the page loads but the product is unavailable.
    /// </summary>
    public bool IsOutOfStock { get; init; }

    /// <summary>
    /// The page title, used for soft-404 detection in ScrapeHealthAnalyzer.
    /// </summary>
    public string? PageTitle { get; init; }

    public static ScrapingResult Failure(string error) => new() { Success = false, Error = error, ErrorCategory = ScrapeErrorCategory.Unknown };

    public static ScrapingResult Failure(string error, ScrapeErrorCategory category, int? httpStatusCode = null) =>
        new() { Success = false, Error = error, ErrorCategory = category, HttpStatusCode = httpStatusCode };
}
