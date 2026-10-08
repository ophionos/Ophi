using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Ophi.Infrastructure.Net;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Infrastructure.Scraping;

/// <summary>
/// Scraping service that uses Playwright for JavaScript-heavy sites.
/// This service uses a headless browser to render the page before extraction.
/// </summary>
public class PlaywrightScrapingService(
    ILogger<PlaywrightScrapingService> logger,
    IStoreConfigProvider configProvider,
    IPlaywrightBrowserManager browserManager,
    Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null) : IScrapingService
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve = resolve ?? Dns.GetHostAddressesAsync;

    /// <summary>
    /// The browser's SSRF control is <see cref="PinnedSocksProxy"/>, which refuses every blocked
    /// connection, but Chromium reports that refusal as ERR_SOCKS_CONNECTION_FAILED — the same code as a
    /// down host. This pre-check gives the common case (the product URL itself is private) its honest
    /// <see cref="ScrapeErrorCategory.BlockedDestination"/> before a page opens. A blocked redirect hop
    /// or sub-resource is still refused by the proxy; it just surfaces as a generic failure.
    /// </summary>
    private async Task<ScrapingResult?> RefuseBlockedStartUrlAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !await PinnedConnector.IsRefusedAsync(uri.Host, _resolve, AddressPolicy.IsBlocked, cancellationToken))
        {
            return null;
        }

        logger.LogWarning("Refused to load {Url} in the browser: it resolves to a private or reserved address", url);
        return ScrapingResult.Failure(PublicAddressHandler.BlockedMessage, ScrapeErrorCategory.BlockedDestination);
    }

    private static Regex GetOrCreateRegex(string pattern) => ScrapeHelpers.GetOrCreateRegex(pattern);

    /// <summary>
    /// Playwright's .NET API takes timeouts, not <see cref="CancellationToken"/>s, so a cancelled
    /// scrape would otherwise keep running to <c>GotoAsync</c>'s own 60s timeout and stall graceful
    /// shutdown. Closing the context is the supported way to abort in flight: pending navigation and
    /// evaluation calls fail immediately. Fire-and-forget — the scrape's own <c>finally</c> also
    /// closes the context, and <c>CloseAsync</c> is idempotent.
    /// </summary>
    private static void AbortOnCancellation(object? state)
    {
        _ = SafeCloseAsync((IBrowserContext)state!);

        static async Task SafeCloseAsync(IBrowserContext context)
        {
            try { await context.CloseAsync(); } catch { /* already closing, or the browser is gone */ }
        }
    }

    public async Task<ScrapingResult> ScrapeWithConfigAsync(string url, StoreConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            if (await RefuseBlockedStartUrlAsync(url, cancellationToken) is { } refused)
                return refused;

            logger.LogDebug("Using Playwright with provided config {StoreId} for {Url}", config.Id, url);

            var page = await browserManager.NewPageAsync(config.CustomUserAgent);
            var context = page.Context;
            await using var cancelRegistration = cancellationToken.Register(AbortOnCancellation, context);
            try
            {
                var response = await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.Load,
                    Timeout = 60000
                });

                // Check HTTP status from Playwright response
                if (response != null && response.Status >= 400)
                {
                    return ScrapingService.ClassifyHttpError(response.Status);
                }

                try
                {
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                        new PageWaitForLoadStateOptions { Timeout = 15000 });
                }
                catch (TimeoutException)
                {
                    logger.LogDebug("NetworkIdle not reached for {Url}, proceeding with current page state", url);
                }

                var antiBotResolved = await WaitForAntiBot(page);
                if (!antiBotResolved)
                {
                    return ScrapingResult.Failure("Blocked by anti-bot protection", ScrapeErrorCategory.AntiBot);
                }

                var selectorConfig = config.Selectors;
                var priceLocale = config.PriceLocale;
                var jsonLdContents = await GetJsonLdContentsAsync(page);
                var price = await ExtractPriceAsync(page, null, selectorConfig, priceLocale, config.CurrencyOverride, jsonLdContents);
                var name = await ExtractNameAsync(page, selectorConfig, jsonLdContents);
                var imageUrl = await ExtractImageUrlAsync(page, url, selectorConfig, jsonLdContents);
                var pageTitle = await page.TitleAsync();
                var isOutOfStock = await DetectOutOfStockAsync(page, selectorConfig, jsonLdContents);

                // No purchasable offer for this locale (e.g. geo shipping restriction) → the page has
                // no buy-box price, so any value a price selector matched is a stray accessory/comparison
                // cell. Discard it and record the listing as unavailable.
                if (ScrapingService.IsNoPurchasableOffer(await page.InnerTextAsync("body"), selectorConfig?.UnavailableTextPatterns ?? CommonSelectors.UnavailableTextPatterns))
                {
                    price = null;
                    isOutOfStock = true;
                }

                var configFinalUrl = page.Url;

                if (price == null)
                {
                    if (isOutOfStock)
                    {
                        var metaCurrency = config.CurrencyOverride ?? await ExtractCurrencyAsync(page, jsonLdContents);
                        return new ScrapingResult
                        {
                            Success = true,
                            IsOutOfStock = true,
                            Name = name,
                            ImageUrl = imageUrl,
                            Currency = metaCurrency,
                            StoreId = config.Id,
                            FinalUrl = configFinalUrl,
                            ErrorCategory = ScrapeErrorCategory.OutOfStock,
                            PageTitle = pageTitle
                        };
                    }

                    return ScrapingResult.Failure("Could not extract price from page", ScrapeErrorCategory.ParseError);
                }

                return new ScrapingResult
                {
                    Success = true,
                    IsOutOfStock = isOutOfStock,
                    Name = name,
                    ImageUrl = imageUrl,
                    Price = price.Value.Amount,
                    Currency = price.Value.Currency,
                    DetectedSelector = price.Value.Selector,
                    StoreId = config.Id,
                    FinalUrl = configFinalUrl,
                    PageTitle = pageTitle
                };
            }
            finally
            {
                await context.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            // A cancelled scrape aborts by closing the context, so the failure surfacing here is the
            // abort itself, not a problem with the page. Reporting it as a scrape failure would
            // budget a host shutdown against the URL's auto-pause count.
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogError(ex, "Error scraping product from {Url} with Playwright using config {StoreId}", url, config.Id);
            return ScrapingResult.Failure($"Scraping error: {ex.Message}");
        }
    }

    public async Task<ScrapingResult> ScrapeProductAsync(string url, string? selector = null, Guid? userId = null, bool captureHtml = false, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get store-specific or generic config (user configs take precedence)
            var storeConfig = userId.HasValue
                ? await configProvider.GetConfigForUrlAsync(url, userId.Value, cancellationToken)
                : configProvider.GetConfigForUrl(url);
            var config = storeConfig?.Selectors ?? configProvider.GetGenericConfig().Selectors;
            var storeId = storeConfig?.Id ?? "generic";

            if (await RefuseBlockedStartUrlAsync(url, cancellationToken) is { } refused)
                return refused;

            logger.LogDebug("Using Playwright with {StoreId} adapter for {Url}", storeId, url);

            var page = await browserManager.NewPageAsync(storeConfig?.CustomUserAgent);
            var context = page.Context;
            await using var cancelRegistration = cancellationToken.Register(AbortOnCancellation, context);
            try
            {
                // Navigate and wait for the load event.
                // Then try for NetworkIdle but don't fail if it's not reached.
                var response = await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.Load,
                    Timeout = 60000
                });

                // Check HTTP status from Playwright response
                if (response != null && response.Status >= 400)
                {
                    return ScrapingService.ClassifyHttpError(response.Status);
                }

                // Wait for NetworkIdle briefly — many sites never reach it
                try
                {
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                        new PageWaitForLoadStateOptions { Timeout = 15000 });
                }
                catch (TimeoutException)
                {
                    logger.LogDebug("NetworkIdle not reached for {Url}, proceeding with current page state", url);
                }

                // Detect and wait out Cloudflare/anti-bot challenge pages
                var antiBotResolved = await WaitForAntiBot(page);
                if (!antiBotResolved)
                {
                    return ScrapingResult.Failure("Blocked by anti-bot protection", ScrapeErrorCategory.AntiBot);
                }

                // Extract data using JavaScript evaluation
                var priceLocale = storeConfig?.PriceLocale ?? "en-US";
                var currencyOverride = storeConfig?.CurrencyOverride;
                var jsonLdContents = await GetJsonLdContentsAsync(page);
                var price = await ExtractPriceAsync(page, selector, config, priceLocale, currencyOverride, jsonLdContents);
                var name = await ExtractNameAsync(page, config, jsonLdContents);
                var imageUrl = await ExtractImageUrlAsync(page, url, config, jsonLdContents);

                // If store-specific extraction failed, try generic fallback
                // Store config's locale and currency override still apply to generic selectors
                if (price == null && storeConfig != null)
                {
                    logger.LogDebug("Store-specific extraction failed for {Url}, trying generic fallback", url);
                    var genericConfig = configProvider.GetGenericConfig().Selectors;
                    price = await ExtractPriceAsync(page, selector, genericConfig, priceLocale, currencyOverride, jsonLdContents);
                    name ??= await ExtractNameAsync(page, genericConfig, jsonLdContents);
                    imageUrl ??= await ExtractImageUrlAsync(page, url, genericConfig, jsonLdContents);
                }

                var productFinalUrl = page.Url;
                var pageTitle = await page.TitleAsync();
                var isOutOfStock = await DetectOutOfStockAsync(page, config, jsonLdContents);

                // No purchasable offer for this locale (e.g. geo shipping restriction) → discard any
                // stray matched price and record the listing as unavailable.
                if (ScrapingService.IsNoPurchasableOffer(await page.InnerTextAsync("body"), config.UnavailableTextPatterns ?? CommonSelectors.UnavailableTextPatterns))
                {
                    price = null;
                    isOutOfStock = true;
                }

                // For a first-time generic scrape, hand the worker the rendered DOM so it can infer a
                // store config. Captured once here while the page is live, then reused for both the OOS
                // and success returns below. Gated on captureHtml so recurring scrapes (and store-config
                // scrapes, which never auto-create) don't pay the extra ContentAsync round-trip.
                var fetchedHtml = captureHtml && storeId == "generic"
                    ? await page.ContentAsync()
                    : null;

                if (price == null)
                {
                    if (isOutOfStock)
                    {
                        var metaCurrency = currencyOverride ?? await ExtractCurrencyAsync(page, jsonLdContents);
                        return new ScrapingResult
                        {
                            Success = true,
                            IsOutOfStock = true,
                            Name = name,
                            ImageUrl = imageUrl,
                            Currency = metaCurrency,
                            StoreId = storeId,
                            FinalUrl = productFinalUrl,
                            FetchedHtml = fetchedHtml,
                            ErrorCategory = ScrapeErrorCategory.OutOfStock,
                            PageTitle = pageTitle
                        };
                    }

                    // Log diagnostics to help debug extraction failures
                    logger.LogWarning(
                        "Price extraction failed for {Url}. Page title: '{Title}', Final URL: '{FinalUrl}', " +
                        "StoreId: {StoreId}, Selectors tried: [{Selectors}]",
                        url, pageTitle, productFinalUrl, storeId,
                        string.Join(", ", config.PriceSelectors));
                    return ScrapingResult.Failure("Could not extract price from page", ScrapeErrorCategory.ParseError);
                }

                return new ScrapingResult
                {
                    Success = true,
                    IsOutOfStock = isOutOfStock,
                    Name = name,
                    ImageUrl = imageUrl,
                    Price = price.Value.Amount,
                    Currency = price.Value.Currency,
                    DetectedSelector = selector ?? price.Value.Selector,
                    StoreId = storeId,
                    FinalUrl = productFinalUrl,
                    FetchedHtml = fetchedHtml,
                    PageTitle = pageTitle
                };
            }
            finally
            {
                await context.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            // See ScrapeWithConfigAsync: an abort-by-context-close must surface as cancellation, not
            // as a scrape failure that counts against the URL's auto-pause budget.
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogError(ex, "Error scraping product from {Url} with Playwright", url);
            return ScrapingResult.Failure($"Scraping error: {ex.Message}");
        }
    }

    /// <summary>
    /// Detects out-of-stock status from page content using Playwright.
    /// Checks JSON-LD availability, Schema.org microdata, and common CSS patterns.
    /// </summary>
    private async Task<bool> DetectOutOfStockAsync(IPage page, StoreSelectorConfig? config, string[]? jsonLdContents = null)
    {
        // 1. Check JSON-LD Schema.org availability via shared JsonPathExtractor.
        // Scan ALL availability matches so an OOS value anywhere in any block trips the flag.
        jsonLdContents ??= await GetJsonLdContentsAsync(page);
        foreach (var availability in JsonPathExtractor.ExtractAvailability(jsonLdContents))
        {
            if (CommonSelectors.OutOfStockSchemaValues.Any(v =>
                    availability.Contains(v, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        // 2. Check CSS selectors with text pattern matching
        var selectors = config?.OutOfStockSelectors ?? CommonSelectors.OutOfStockSelectors;
        var textPatterns = config?.OutOfStockTextPatterns ?? CommonSelectors.OutOfStockTextPatterns;

        foreach (var selectorSpec in selectors)
        {
            try
            {
                var (cssSelector, attributeName) = ScrapeHelpers.ParseSelectorSpec(selectorSpec);
                var element = await page.QuerySelectorAsync(cssSelector);
                if (element == null) continue;

                var text = attributeName != null
                    ? await element.GetAttributeAsync(attributeName)
                    : await element.TextContentAsync();

                text = text?.Trim();
                if (string.IsNullOrEmpty(text)) continue;

                // Check Schema.org values
                if (CommonSelectors.OutOfStockSchemaValues.Any(v =>
                    text.Contains(v, StringComparison.OrdinalIgnoreCase)))
                    return true;

                // Check text patterns
                if (textPatterns.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Selector extraction failed, trying next");
            }
        }

        return false;
    }

    private async Task<(decimal Amount, string Currency, string Selector)?> ExtractPriceAsync(
        IPage page,
        string? customSelector,
        StoreSelectorConfig config,
        string priceLocale = "en-US",
        string? currencyOverride = null,
        string[]? jsonLdContents = null)
    {
        // Currency priority: store config override > metadata > symbol detection
        var metadataCurrency = currencyOverride ?? await ExtractCurrencyAsync(page, jsonLdContents);

        var selectors = customSelector != null
            ? [customSelector, .. config.PriceSelectors]
            : config.PriceSelectors;

        // Try CSS selectors
        foreach (var selectorSpec in selectors)
        {
            try
            {
                var (cssSelector, attributeName) = ParseSelectorSpec(selectorSpec);

                // Iterate ALL matches for this selector, not just the first. Amazon (and
                // others) render a hidden/placeholder element with empty text ahead of the
                // real price; using only the first match would abandon the selector on that
                // empty element and fall through to a coarser selector like `.a-price-whole`,
                // which yields the integer part with no currency symbol (e.g. "17." -> 17.0 USD
                // instead of "€17.91" -> 17.91 EUR). Take the first match that actually parses.
                var elements = await page.QuerySelectorAllAsync(cssSelector);
                foreach (var element in elements)
                {
                    string? priceText;
                    if (attributeName != null)
                    {
                        priceText = await element.GetAttributeAsync(attributeName);
                    }
                    else
                    {
                        // Try common price attributes first
                        priceText = await element.GetAttributeAsync("data-price")
                            ?? await element.GetAttributeAsync("content")
                            ?? await element.TextContentAsync();
                    }

                    if (string.IsNullOrWhiteSpace(priceText)) continue;

                    var parsed = PriceParser.Parse(priceText, priceLocale);
                    if (parsed != null)
                    {
                        var currency = metadataCurrency ?? parsed.Value.Currency;
                        return (parsed.Value.Amount, currency, selectorSpec);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Selector {Selector} failed", selectorSpec);
            }
        }

        // Try JSONPath expressions on JSON-LD blocks
        if (config.PriceJsonPaths is { Length: > 0 })
        {
            jsonLdContents ??= await GetJsonLdContentsAsync(page);
            if (jsonLdContents.Length > 0)
            {
                foreach (var jsonPath in config.PriceJsonPaths)
                {
                    var priceText = JsonPathExtractor.Extract(jsonLdContents, jsonPath);
                    if (priceText == null) continue;

                    var parsed = PriceParser.Parse(priceText, priceLocale);
                    if (parsed != null)
                    {
                        var currency = metadataCurrency ?? parsed.Value.Currency;
                        return (parsed.Value.Amount, currency, $"jsonpath:{jsonPath}");
                    }
                }
            }
        }

        // Fall back to regex patterns
        if (config.PriceRegexPatterns is { Length: > 0 })
        {
            var html = await page.ContentAsync();
            foreach (var pattern in config.PriceRegexPatterns)
            {
                try
                {
                    var regex = GetOrCreateRegex(pattern);
                    var match = regex.Match(html);
                    if (match is { Success: true, Groups.Count: > 1 })
                    {
                        var priceText = match.Groups[1].Value;
                        var parsed = PriceParser.Parse(priceText, priceLocale);
                        if (parsed != null)
                        {
                            var currency = metadataCurrency ?? parsed.Value.Currency;
                            return (parsed.Value.Amount, currency, $"regex:{pattern}");
                        }
                    }
                }
                catch (RegexParseException ex)
                {
                    logger.LogWarning(ex, "Invalid regex pattern: {Pattern}", pattern);
                }
                catch (RegexMatchTimeoutException ex)
                {
                    logger.LogWarning(ex, "Regex match timed out for pattern: {Pattern}", pattern);
                }
            }
        }

        return null;
    }

    private static async Task<string?> ExtractNameAsync(IPage page, StoreSelectorConfig config, string[]? jsonLdContents = null)
    {
        foreach (var selectorSpec in config.NameSelectors)
        {
            try
            {
                var (cssSelector, attributeName) = ParseSelectorSpec(selectorSpec);

                var element = await page.QuerySelectorAsync(cssSelector);
                if (element == null) continue;

                var name = attributeName != null
                    ? await element.GetAttributeAsync(attributeName)
                    : await element.TextContentAsync();

                name = name?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    return name.Length > 500 ? name[..500] : name;
                }
            }
            catch
            {
                // Continue to next selector
            }
        }

        // Try JSONPath expressions on JSON-LD blocks
        if (config.NameJsonPaths is { Length: > 0 })
        {
            jsonLdContents ??= await GetJsonLdContentsAsync(page);
            var name = JsonPathExtractor.ExtractFirst(jsonLdContents, config.NameJsonPaths)?.Trim();
            if (!string.IsNullOrEmpty(name))
                return name.Length > 500 ? name[..500] : name;
        }

        return null;
    }

    private async Task<string?> ExtractImageUrlAsync(IPage page, string baseUrl, StoreSelectorConfig config, string[]? jsonLdContents = null)
    {
        foreach (var selectorSpec in config.ImageSelectors)
        {
            try
            {
                var (cssSelector, attributeName) = ParseSelectorSpec(selectorSpec);

                var element = await page.QuerySelectorAsync(cssSelector);
                if (element == null) continue;

                string? src;
                if (attributeName != null)
                {
                    src = await element.GetAttributeAsync(attributeName);
                }
                else
                {
                    src = await element.GetAttributeAsync("src")
                        ?? await element.GetAttributeAsync("data-src")
                        ?? await element.GetAttributeAsync("data-lazy-src")
                        ?? await element.GetAttributeAsync("content");
                }

                if (!string.IsNullOrEmpty(src))
                {
                    return NormalizeImageUrl(src, baseUrl);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Selector extraction failed, trying next");
            }
        }

        // Try JSONPath expressions on JSON-LD blocks
        if (config.ImageJsonPaths is { Length: > 0 })
        {
            jsonLdContents ??= await GetJsonLdContentsAsync(page);
            var src = JsonPathExtractor.ExtractFirst(jsonLdContents, config.ImageJsonPaths);
            if (!string.IsNullOrEmpty(src))
                return NormalizeImageUrl(src, baseUrl);
        }

        // Fall back to regex patterns
        if (config.ImageRegexPatterns is { Length: > 0 })
        {
            var html = await page.ContentAsync();
            foreach (var pattern in config.ImageRegexPatterns)
            {
                try
                {
                    var regex = GetOrCreateRegex(pattern);
                    var match = regex.Match(html);
                    if (match is { Success: true, Groups.Count: > 1 })
                    {
                        var src = match.Groups[1].Value;
                        if (!string.IsNullOrEmpty(src))
                        {
                            return NormalizeImageUrl(src, baseUrl);
                        }
                    }
                }
                catch (RegexParseException ex)
                {
                    logger.LogWarning(ex, "Invalid regex pattern: {Pattern}", pattern);
                }
                catch (RegexMatchTimeoutException ex)
                {
                    logger.LogWarning(ex, "Regex match timed out for pattern: {Pattern}", pattern);
                }
            }
        }

        return null;
    }

    private static async Task<string?> ExtractCurrencyAsync(IPage page, string[]? jsonLdContents = null)
    {
        // Try CSS selectors first (meta tags, microdata) using shared selectors
        foreach (var selectorSpec in CommonSelectors.CurrencySelectors)
        {
            try
            {
                var (cssSelector, attributeName) = ParseSelectorSpec(selectorSpec);
                var element = await page.QuerySelectorAsync(cssSelector);
                if (element == null) continue;

                var currency = attributeName != null
                    ? await element.GetAttributeAsync(attributeName)
                    : await element.TextContentAsync();

                currency = currency?.Trim().ToUpperInvariant();
                if (!string.IsNullOrEmpty(currency) && currency.Length == 3)
                {
                    return currency;
                }
            }
            catch
            {
                // Continue to next selector
            }
        }

        // Fall back to JSON-LD structured data via shared JsonPathExtractor
        jsonLdContents ??= await GetJsonLdContentsAsync(page);
        return JsonPathExtractor.ExtractCurrencyCode(jsonLdContents);
    }

    private static (string Selector, string? AttributeName) ParseSelectorSpec(string selectorSpec) =>
        ScrapeHelpers.ParseSelectorSpec(selectorSpec);

    private static string NormalizeImageUrl(string src, string baseUrl) =>
        ScrapeHelpers.NormalizeImageUrl(src, baseUrl);

    /// <summary>
    /// Extracts JSON-LD content from the page via JavaScript evaluation.
    /// </summary>
    private static async Task<string[]> GetJsonLdContentsAsync(IPage page)
    {
        try
        {
            return await page.EvaluateAsync<string[]>("""
                () => Array.from(document.querySelectorAll('script[type="application/ld+json"]'))
                      .map(s => s.textContent)
                      .filter(t => t && t.trim())
                """) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Detects Cloudflare or similar anti-bot challenge pages and waits for them to resolve.
    /// Returns true if no challenge or challenge resolved, false if blocked.
    /// </summary>
    private async Task<bool> WaitForAntiBot(IPage page)
    {
        var title = await page.TitleAsync();
        if (!AntiBotSignals.IsChallenge(title, page.Url))
            return true;

        // A redirect to a challenge path never un-redirects, so there is nothing to wait for. Bail
        // now rather than spending the timeout on a page that cannot resolve.
        if (AntiBotSignals.IsChallengeUrl(page.Url))
        {
            logger.LogWarning("Blocked by anti-bot redirect to {Url}", page.Url);
            return false;
        }

        logger.LogDebug("Anti-bot challenge detected (title: '{Title}'), waiting for resolution...", title);

        // Wait up to 20 seconds for the challenge to resolve
        try
        {
            // The challenge page title changes once it resolves. Compared case-insensitively against
            // the same list the HTTP path uses, so the two paths can't drift apart on what counts as
            // a challenge. Note that only Cloudflare's interstitial self-resolves — a PerimeterX
            // challenge needs a human, so it burns the full timeout before being reported.
            await page.WaitForFunctionAsync(
                "titles => !titles.some(t => t.toLowerCase() === document.title.trim().toLowerCase())",
                AntiBotSignals.ChallengeTitles,
                new PageWaitForFunctionOptions { Timeout = 20000 });

            // After the challenge resolves, wait for the real page to load
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded,
                new PageWaitForLoadStateOptions { Timeout = 15000 });

            // Re-check rather than trusting the wait: its predicate only watches the title, so a
            // challenge that resolves the title while leaving us on a block URL would otherwise be
            // reported as cleared.
            var resolved = !AntiBotSignals.IsChallenge(await page.TitleAsync(), page.Url);
            logger.LogDebug("Anti-bot challenge {Outcome}, page title now: '{Title}'",
                resolved ? "resolved" : "still present", await page.TitleAsync());
            return resolved;
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Anti-bot challenge did not resolve within timeout for {Url}", page.Url);
            return false;
        }
    }
}
