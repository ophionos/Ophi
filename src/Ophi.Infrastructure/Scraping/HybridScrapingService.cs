using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Infrastructure.Scraping;

/// <summary>
/// A scraping service that tries HTTP-based scraping first, then falls back to Playwright
/// for JavaScript-heavy sites or when HTTP scraping fails.
/// </summary>
public class HybridScrapingService : IScrapingService
{
    private readonly IScrapingService _httpService;
    private readonly IScrapingService _playwrightService;
    private readonly IStoreConfigProvider _configProvider;
    private readonly ILogger<HybridScrapingService> _logger;

    /// <summary>
    /// Constructor for dependency injection using keyed services.
    /// </summary>
    public HybridScrapingService(
        [FromKeyedServices("http")] IScrapingService httpService,
        [FromKeyedServices("playwright")] IScrapingService playwrightService,
        IStoreConfigProvider configProvider,
        ILogger<HybridScrapingService> logger)
    {
        _httpService = httpService;
        _playwrightService = playwrightService;
        _configProvider = configProvider;
        _logger = logger;
    }

    public async Task<ScrapingResult> ScrapeWithConfigAsync(string url, StoreConfig config, CancellationToken cancellationToken = default)
    {
        ScrapingResult result;

        if (config.RequiresJavaScript)
        {
            _logger.LogDebug("Config {StoreId} requires JavaScript, using Playwright for {Url}", config.Id, url);
            result = await _playwrightService.ScrapeWithConfigAsync(url, config, cancellationToken);
        }
        else
        {
            // Try HTTP first
            _logger.LogDebug("Trying HTTP scraping with config {StoreId} for {Url}", config.Id, url);
            result = await _httpService.ScrapeWithConfigAsync(url, config, cancellationToken);

            // If HTTP scraping failed, try Playwright as fallback
            // Don't fall back if OOS was detected — the HTTP scrape worked correctly
            if (!result.Success || (!result.IsOutOfStock && (result.Name == "Unknown Product" || result.Price == null)))
            {
                _logger.LogDebug("HTTP scraping incomplete for {Url}, falling back to Playwright", url);
                var playwrightResult = await _playwrightService.ScrapeWithConfigAsync(url, config, cancellationToken);

                if (playwrightResult.Success &&
                    (playwrightResult.Name != "Unknown Product" || playwrightResult.Price != null || playwrightResult.IsOutOfStock))
                {
                    result = playwrightResult;
                }
            }
        }

        return ApplyCurrencyOverride(result, config);
    }

    public async Task<ScrapingResult> ScrapeProductAsync(string url, string? selector = null, Guid? userId = null, bool captureHtml = false, CancellationToken cancellationToken = default)
    {
        // Resolve store config — use user-specific configs when userId is available
        var storeConfig = userId.HasValue
            ? await _configProvider.GetConfigForUrlAsync(url, userId.Value, cancellationToken)
            : _configProvider.GetConfigForUrl(url);

        var storeId = storeConfig?.Id ?? "generic";

        // Single source of truth: per-store config decides JS requirement
        var requiresJs = storeConfig?.RequiresJavaScript ?? false;

        ScrapingResult result;

        if (requiresJs)
        {
            _logger.LogDebug("Store {StoreId} requires JavaScript, using Playwright for {Url}", storeId, url);
            result = await _playwrightService.ScrapeProductAsync(url, selector, userId, captureHtml, cancellationToken);
        }
        else
        {
            // Try HTTP first
            _logger.LogDebug("Trying HTTP scraping for {Url}", url);
            result = await _httpService.ScrapeProductAsync(url, selector, userId, captureHtml, cancellationToken);

            // If HTTP scraping failed to get useful data, try Playwright as fallback
            // Don't fall back if OOS was detected — the HTTP scrape worked correctly
            if (!result.Success || (!result.IsOutOfStock && (result.Name == "Unknown Product" || result.Price == null)))
            {
                _logger.LogDebug("HTTP scraping incomplete for {Url}, falling back to Playwright", url);
                var playwrightResult = await _playwrightService.ScrapeProductAsync(url, selector, userId, captureHtml, cancellationToken);

                // Use Playwright result if it's better
                if (playwrightResult.Success &&
                    (playwrightResult.Name != "Unknown Product" || playwrightResult.Price != null || playwrightResult.IsOutOfStock))
                {
                    result = playwrightResult;
                }
            }
        }

        return ApplyCurrencyOverride(result, storeConfig);
    }

    private static ScrapingResult ApplyCurrencyOverride(ScrapingResult result, StoreConfig? config) =>
        config?.CurrencyOverride is { } currencyOverride ? result with { Currency = currencyOverride } : result;
}
