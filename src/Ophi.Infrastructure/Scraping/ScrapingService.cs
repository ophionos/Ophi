using System.Net;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Infrastructure.Scraping;

public class ScrapingService(HttpClient httpClient, ILogger<ScrapingService> logger, IStoreConfigProvider configProvider) : IScrapingService
{
    internal record FetchResult(string? Html, string? FinalUrl, int StatusCode, bool IsSuccess);

    // captureHtml is part of the interface but moot here: the HTTP fetch already holds the page HTML in
    // memory, so the generic path returns FetchedHtml unconditionally (no extra round-trip to gate).
    public async Task<ScrapingResult> ScrapeProductAsync(string url, string? selector = null, Guid? userId = null, bool captureHtml = false, CancellationToken cancellationToken = default)
    {
        try
        {
            // Resolve store config FIRST so we can use any per-store custom UA for the fetch
            var storeConfig = userId.HasValue
                ? await configProvider.GetConfigForUrlAsync(url, userId.Value, cancellationToken)
                : configProvider.GetConfigForUrl(url);

            var fetch = await FetchPageAsync(url, cancellationToken, storeConfig?.CustomUserAgent);

            if (!fetch.IsSuccess)
            {
                return ClassifyHttpError(fetch.StatusCode);
            }

            if (string.IsNullOrEmpty(fetch.Html))
            {
                return ScrapingResult.Failure("Failed to fetch page content", ScrapeErrorCategory.ParseError, fetch.StatusCode);
            }

            var config = storeConfig?.Selectors ?? configProvider.GetGenericConfig().Selectors;
            var storeId = storeConfig?.Id ?? "generic";

            logger.LogDebug("Using {StoreId} adapter for {Url}", storeId, url);

            var browsingConfig = Configuration.Default;
            using var context = BrowsingContext.New(browsingConfig);
            var document = await context.OpenAsync(req => req.Content(fetch.Html), cancellationToken);

            var challenge = DetectChallenge(document, url, fetch);
            if (challenge != null)
            {
                return challenge;
            }

            var priceLocale = storeConfig?.PriceLocale ?? "en-US";
            var currencyOverride = storeConfig?.CurrencyOverride;
            var jsonLdContents = JsonPathExtractor.GetJsonLdContents(document);
            var price = ExtractPrice(document, selector, config, priceLocale, currencyOverride, jsonLdContents);
            var name = ExtractName(document, config, jsonLdContents);
            var imageUrl = ExtractImageUrl(document, url, config, jsonLdContents);
            var pageTitle = document.Title;

            // If store-specific extraction failed, try generic fallback
            // Store config's locale and currency override still apply to generic selectors
            if (price == null && storeConfig != null)
            {
                logger.LogDebug("Store-specific extraction failed for {Url}, trying generic fallback", url);
                var genericConfig = configProvider.GetGenericConfig().Selectors;
                price = ExtractPrice(document, selector, genericConfig, priceLocale, currencyOverride, jsonLdContents);
                name ??= ExtractName(document, genericConfig, jsonLdContents);
                imageUrl ??= ExtractImageUrl(document, url, genericConfig, jsonLdContents);
            }

            // Check for out-of-stock indicators
            var isOutOfStock = DetectOutOfStock(document, config, jsonLdContents);

            // No purchasable offer for this locale (e.g. geo shipping restriction) → the page has no
            // buy-box price, so anything a price selector matched is a stray accessory/comparison cell.
            // Discard it and record the listing as unavailable.
            if (IsNoPurchasableOffer(document.Body?.TextContent, config?.UnavailableTextPatterns ?? CommonSelectors.UnavailableTextPatterns))
            {
                price = null;
                isOutOfStock = true;
            }

            if (price == null)
            {
                if (isOutOfStock)
                {
                    // OOS with no visible price — the scrape worked, the product is just unavailable
                    var metaCurrency = currencyOverride ?? ExtractCurrency(document);
                    return new ScrapingResult
                    {
                        Success = true,
                        IsOutOfStock = true,
                        Name = name,
                        ImageUrl = imageUrl,
                        Currency = metaCurrency,
                        StoreId = storeId,
                        FinalUrl = fetch.FinalUrl,
                        HttpStatusCode = fetch.StatusCode,
                        ErrorCategory = ScrapeErrorCategory.OutOfStock,
                        PageTitle = pageTitle,
                        FetchedHtml = storeId == "generic" ? fetch.Html : null
                    };
                }

                return ScrapingResult.Failure("Could not extract price from page", ScrapeErrorCategory.ParseError, fetch.StatusCode);
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
                FetchedHtml = storeId == "generic" ? fetch.Html : null,
                FinalUrl = fetch.FinalUrl,
                HttpStatusCode = fetch.StatusCode,
                PageTitle = pageTitle
            };
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error scraping {Url}", url);
            return ScrapingResult.Failure($"Network error: {ex.Message}", ScrapeErrorCategory.NetworkError);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Request timed out for {Url}", url);
            return ScrapingResult.Failure("Request timed out", ScrapeErrorCategory.NetworkError);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error scraping product from {Url}", url);
            return ScrapingResult.Failure($"Scraping error: {ex.Message}");
        }
    }

    public async Task<ScrapingResult> ScrapeWithConfigAsync(string url, StoreConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            var fetch = await FetchPageAsync(url, cancellationToken, config.CustomUserAgent);

            if (!fetch.IsSuccess)
            {
                return ClassifyHttpError(fetch.StatusCode);
            }

            if (string.IsNullOrEmpty(fetch.Html))
            {
                return ScrapingResult.Failure("Failed to fetch page content", ScrapeErrorCategory.ParseError, fetch.StatusCode);
            }

            return await ParseWithConfigAsync(
                fetch.Html, config, url, fetch.FinalUrl, fetch.StatusCode, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error scraping {Url} with config {StoreId}", url, config.Id);
            return ScrapingResult.Failure($"Network error: {ex.Message}", ScrapeErrorCategory.NetworkError);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Request timed out for {Url} with config {StoreId}", url, config.Id);
            return ScrapingResult.Failure("Request timed out", ScrapeErrorCategory.NetworkError);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error scraping product from {Url} with config {StoreId}", url, config.Id);
            return ScrapingResult.Failure($"Scraping error: {ex.Message}");
        }
    }

    /// <summary>
    /// The fetch-free half of <see cref="ScrapeWithConfigAsync"/>: everything from "here is the HTML"
    /// to a <see cref="ScrapingResult"/>. Split out so a <see cref="StoreConfig"/> can be verified
    /// against a saved page with no network at all — which is what lets someone whose connection can
    /// reach a site contribute and prove an adapter that this project's own egress cannot load
    /// (issue #136). Challenge detection runs here rather than in the caller so a captured block
    /// page is reported as such instead of as a parse failure.
    /// </summary>
    internal async Task<ScrapingResult> ParseWithConfigAsync(
        string html, StoreConfig config, string url, string? finalUrl = null, int statusCode = 200,
        CancellationToken cancellationToken = default)
    {
        var fetch = new FetchResult(html, finalUrl, statusCode, true);

        var selectorConfig = config.Selectors;
        var priceLocale = config.PriceLocale;
        var currencyOverride = config.CurrencyOverride;

        var browsingConfig = Configuration.Default;
        using var context = BrowsingContext.New(browsingConfig);
        var document = await context.OpenAsync(req => req.Content(fetch.Html), cancellationToken);

        var challenge = DetectChallenge(document, url, fetch);
        if (challenge != null)
        {
            return challenge;
        }

        var jsonLdContents = JsonPathExtractor.GetJsonLdContents(document);
        var price = ExtractPrice(document, null, selectorConfig, priceLocale, currencyOverride, jsonLdContents);
        var name = ExtractName(document, selectorConfig, jsonLdContents);
        var imageUrl = ExtractImageUrl(document, url, selectorConfig, jsonLdContents);
        var pageTitle = document.Title;
        var isOutOfStock = DetectOutOfStock(document, selectorConfig, jsonLdContents);

        // No purchasable offer for this locale (e.g. geo shipping restriction) → discard any
        // stray matched price and record the listing as unavailable.
        if (IsNoPurchasableOffer(document.Body?.TextContent, selectorConfig?.UnavailableTextPatterns ?? CommonSelectors.UnavailableTextPatterns))
        {
            price = null;
            isOutOfStock = true;
        }

        if (price == null)
        {
            if (isOutOfStock)
            {
                var metaCurrency = currencyOverride ?? ExtractCurrency(document);
                return new ScrapingResult
                {
                    Success = true,
                    IsOutOfStock = true,
                    Name = name,
                    ImageUrl = imageUrl,
                    Currency = metaCurrency,
                    StoreId = config.Id,
                    FinalUrl = fetch.FinalUrl,
                    HttpStatusCode = fetch.StatusCode,
                    ErrorCategory = ScrapeErrorCategory.OutOfStock,
                    PageTitle = pageTitle
                };
            }

            return ScrapingResult.Failure("Could not extract price from page", ScrapeErrorCategory.ParseError, fetch.StatusCode);
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
            FinalUrl = fetch.FinalUrl,
            HttpStatusCode = fetch.StatusCode,
            PageTitle = pageTitle
        };
    }

    internal static ScrapingResult ClassifyHttpError(int statusCode)
    {
        var category = statusCode switch
        {
            404 or 410 => ScrapeErrorCategory.NotFound,
            403 => ScrapeErrorCategory.Forbidden,
            429 => ScrapeErrorCategory.RateLimited,
            >= 500 => ScrapeErrorCategory.ServerError,
            _ => ScrapeErrorCategory.Unknown
        };
        return ScrapingResult.Failure($"HTTP {statusCode}", category, statusCode);
    }

    private const long MaxResponseSizeBytes = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    /// Returns an <see cref="ScrapeErrorCategory.AntiBot"/> failure when the parsed page is a
    /// challenge rather than the page we asked for, otherwise null.
    ///
    /// <para>
    /// Called by every fetch-and-parse entry point, and shared rather than inlined for a reason:
    /// the first version of this check lived only in <see cref="ScrapeProductAsync"/>, so
    /// <see cref="ScrapeWithConfigAsync"/> — which backs the "test this store config" action — kept
    /// reporting a challenge as "could not extract price from page", sending users off to tune
    /// selectors against a page that was never served. A new entry point must call this too.
    /// </para>
    ///
    /// <para>
    /// Runs before extraction, not after: the challenge arrives as HTTP 200 with valid HTML, so it
    /// survives <c>ClassifyHttpError</c> and would otherwise be indistinguishable from a parse
    /// failure by the time extraction gives up.
    /// </para>
    /// </summary>
    private ScrapingResult? DetectChallenge(IDocument document, string requestedUrl, FetchResult fetch)
    {
        if (!AntiBotSignals.IsChallenge(document.Title, fetch.FinalUrl ?? requestedUrl))
        {
            return null;
        }

        logger.LogWarning(
            "Anti-bot challenge detected for {Url} (title: '{Title}', final URL: {FinalUrl})",
            requestedUrl, document.Title, fetch.FinalUrl);

        return ScrapingResult.Failure(
            "Blocked by anti-bot protection", ScrapeErrorCategory.AntiBot, fetch.StatusCode);
    }

    private async Task<FetchResult> FetchPageAsync(string url, CancellationToken cancellationToken, string? customUserAgent = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Upgrade-Insecure-Requests", "1");

        if (customUserAgent != null)
        {
            // Use conservative headers that don't assume a specific browser engine
            request.Headers.Add("User-Agent", customUserAgent);
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
        }
        else
        {
            var profile = BrowserProfiles.GetRandom();
            request.Headers.Add("User-Agent", profile.UserAgent);
            request.Headers.Add("Accept", profile.Accept);
            request.Headers.Add("Accept-Language", profile.AcceptLanguage);

            if (profile.IsChromium)
            {
                request.Headers.Add("Sec-Ch-Ua", profile.SecChUa!);
                request.Headers.Add("Sec-Ch-Ua-Mobile", profile.SecChUaMobile!);
                request.Headers.Add("Sec-Ch-Ua-Platform", profile.SecChUaPlatform!);
                request.Headers.Add("Sec-Fetch-Dest", "document");
                request.Headers.Add("Sec-Fetch-Mode", "navigate");
                request.Headers.Add("Sec-Fetch-Site", "none");
                request.Headers.Add("Sec-Fetch-User", "?1");
            }
        }

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var statusCode = (int)response.StatusCode;
        var finalUrl = response.RequestMessage?.RequestUri?.ToString();

        if (!response.IsSuccessStatusCode)
        {
            return new FetchResult(null, finalUrl, statusCode, false);
        }

        if (response.Content.Headers.ContentLength > MaxResponseSizeBytes)
        {
            logger.LogWarning("Response from {Url} exceeds size limit ({Size} bytes)", url, response.Content.Headers.ContentLength);
            return new FetchResult(null, finalUrl, statusCode, true);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(new LimitedStream(stream, MaxResponseSizeBytes));
        var html = await reader.ReadToEndAsync(cancellationToken);
        return new FetchResult(html, finalUrl, statusCode, true);
    }

    private (decimal Amount, string Currency, string Selector)? ExtractPrice(
        IDocument document,
        string? customSelector,
        StoreSelectorConfig config,
        string priceLocale = "en-US",
        string? currencyOverride = null,
        string[]? jsonLdContents = null)
    {
        // Currency priority: store config override > metadata > symbol detection
        var metadataCurrency = currencyOverride ?? ExtractCurrency(document);

        var selectors = customSelector != null
            ? [customSelector, .. config.PriceSelectors]
            : config.PriceSelectors;

        // Try CSS selectors first
        foreach (var selectorSpec in selectors)
        {
            try
            {
                // Iterate ALL matches for this selector, not just the first. A hidden or
                // placeholder element with empty text ahead of the real price would otherwise
                // make us abandon the selector and fall through to a coarser one. Take the
                // first match that actually parses. (See PlaywrightScrapingService for the
                // Amazon `.a-price-whole` "17." -> 17.0 USD failure mode this guards against.)
                foreach (var priceText in ExtractAllWithSelector(document, selectorSpec))
                {
                    if (string.IsNullOrWhiteSpace(priceText)) continue;

                    var parsed = PriceParser.Parse(priceText, priceLocale);
                    if (parsed != null)
                    {
                        // Use metadata currency if available, otherwise use detected currency
                        var currency = metadataCurrency ?? parsed.Value.Currency;
                        return (parsed.Value.Amount, currency, selectorSpec);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Selector extraction failed, trying next");
            }
        }

        // Try JSONPath expressions on JSON-LD blocks
        if (config.PriceJsonPaths is { Length: > 0 })
        {
            jsonLdContents ??= JsonPathExtractor.GetJsonLdContents(document);
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

        // Fall back to regex patterns on raw HTML
        if (config.PriceRegexPatterns is { Length: > 0 })
        {
            var html = document.DocumentElement.OuterHtml;
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
                            // Use metadata currency if available, otherwise use detected currency
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

    /// <summary>
    /// True when the page body contains a "no purchasable offer" marker (e.g. an Amazon geo
    /// shipping restriction: "cannot be shipped to your selected delivery location"). In that
    /// state the listing has no buy-box price, so any value a price selector matched is a stray
    /// (an unrelated accessory or comparison cell) and must be discarded. Pure/text-only so the
    /// HTTP (AngleSharp) and Playwright services share identical behaviour.
    /// </summary>
    internal static bool IsNoPurchasableOffer(string? pageText, IReadOnlyList<string> patterns)
    {
        if (string.IsNullOrEmpty(pageText)) return false;
        for (var i = 0; i < patterns.Count; i++)
        {
            if (pageText.Contains(patterns[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Detects whether a product page indicates out-of-stock status by checking
    /// JSON-LD availability, Schema.org microdata, and common CSS patterns.
    /// </summary>
    internal static bool DetectOutOfStock(IDocument document, StoreSelectorConfig? config, string[]? jsonLdContents = null)
    {
        // 1. Check JSON-LD Schema.org availability (highest confidence, machine-readable).
        // Scan ALL availability matches so an OOS value anywhere in any block trips the flag.
        jsonLdContents ??= JsonPathExtractor.GetJsonLdContents(document);
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
                var text = ExtractWithSelectorText(document, selectorSpec)?.Trim();
                if (string.IsNullOrEmpty(text)) continue;

                // Check Schema.org values (e.g., href="https://schema.org/OutOfStock")
                if (CommonSelectors.OutOfStockSchemaValues.Any(v =>
                    text.Contains(v, StringComparison.OrdinalIgnoreCase)))
                    return true;

                // Check text patterns (e.g., "out of stock", "sold out")
                if (textPatterns.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            catch
            {
                // Continue to next selector
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts currency code from page metadata (Open Graph, Schema.org, JSON-LD).
    /// </summary>
    private static string? ExtractCurrency(IDocument document)
    {
        // Try CSS selectors first (meta tags, microdata)
        foreach (var selectorSpec in CommonSelectors.CurrencySelectors)
        {
            try
            {
                var currency = ExtractWithSelectorText(document, selectorSpec)?.Trim().ToUpperInvariant();
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

        // Fall back to JSON-LD structured data
        var jsonLdContents = JsonPathExtractor.GetJsonLdContents(document);
        return JsonPathExtractor.ExtractCurrencyCode(jsonLdContents);
    }

    private static string? ExtractName(IDocument document, StoreSelectorConfig config, string[]? jsonLdContents = null)
    {
        foreach (var selectorSpec in config.NameSelectors)
        {
            try
            {
                var name = ExtractWithSelectorText(document, selectorSpec)?.Trim();
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
            jsonLdContents ??= JsonPathExtractor.GetJsonLdContents(document);
            var name = JsonPathExtractor.ExtractFirst(jsonLdContents, config.NameJsonPaths)?.Trim();
            if (!string.IsNullOrEmpty(name))
                return name.Length > 500 ? name[..500] : name;
        }

        return null;
    }

    private string? ExtractImageUrl(IDocument document, string baseUrl, StoreSelectorConfig config, string[]? jsonLdContents = null)
    {
        // Try CSS selectors first
        foreach (var selectorSpec in config.ImageSelectors)
        {
            try
            {
                var src = ExtractImageWithSelector(document, selectorSpec);
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
            jsonLdContents ??= JsonPathExtractor.GetJsonLdContents(document);
            var src = JsonPathExtractor.ExtractFirst(jsonLdContents, config.ImageJsonPaths);
            if (!string.IsNullOrEmpty(src))
                return NormalizeImageUrl(src, baseUrl);
        }

        // Fall back to regex patterns on raw HTML
        if (config.ImageRegexPatterns is { Length: > 0 })
        {
            var html = document.DocumentElement.OuterHtml;
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

    /// <summary>
    /// Gets a cached regex or creates and caches a new one. Shared with
    /// <see cref="PlaywrightScrapingService"/> so the match timeout can't drift between the two.
    /// </summary>
    private static Regex GetOrCreateRegex(string pattern) => ScrapeHelpers.GetOrCreateRegex(pattern);

    /// <summary>
    /// Extracts price text from every element matching a selector specification, in document
    /// order. Format: "selector" or "selector|attribute" to extract from a specific attribute.
    /// Yields all matches so the caller can skip empty/placeholder elements and take the first
    /// that parses, rather than abandoning the selector on an empty first match.
    /// </summary>
    private static IEnumerable<string?> ExtractAllWithSelector(IDocument document, string selectorSpec)
    {
        var (selector, attributeName) = ParseSelectorSpec(selectorSpec);

        foreach (var element in document.QuerySelectorAll(selector))
        {
            yield return attributeName != null
                ? element.GetAttribute(attributeName)
                // Default attribute extraction order for price elements
                : element.GetAttribute("data-price")
                    ?? element.GetAttribute("content")
                    ?? element.TextContent;
        }
    }

    /// <summary>
    /// Extracts text from an element, supporting "selector|attribute" format.
    /// Unlike ExtractAllWithSelector, defaults to TextContent instead of data-price/content.
    /// </summary>
    private static string? ExtractWithSelectorText(IDocument document, string selectorSpec)
    {
        var (selector, attributeName) = ParseSelectorSpec(selectorSpec);

        var element = document.QuerySelector(selector);
        if (element == null) return null;

        return attributeName != null
            ? element.GetAttribute(attributeName)
            : element.TextContent;
    }

    /// <summary>
    /// Extracts image URL from an element, supporting "selector|attribute" format.
    /// </summary>
    private static string? ExtractImageWithSelector(IDocument document, string selectorSpec)
    {
        var (selector, attributeName) = ParseSelectorSpec(selectorSpec);

        var element = document.QuerySelector(selector);
        if (element == null) return null;

        if (attributeName != null)
        {
            return element.GetAttribute(attributeName);
        }

        // Default attribute extraction order for image elements
        return element.GetAttribute("src")
            ?? element.GetAttribute("data-src")
            ?? element.GetAttribute("data-lazy-src")
            ?? element.GetAttribute("content");
    }

    private static (string Selector, string? AttributeName) ParseSelectorSpec(string selectorSpec) =>
        ScrapeHelpers.ParseSelectorSpec(selectorSpec);

    private static string NormalizeImageUrl(string src, string baseUrl) =>
        ScrapeHelpers.NormalizeImageUrl(src, baseUrl);
}
