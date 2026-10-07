using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Infrastructure.Scraping;

public partial class AutoCreateStoreService(ILogger<AutoCreateStoreService> logger) : IAutoCreateStoreService
{
    public async Task<AutoCreateStoreResult?> AnalyzeHtmlAsync(string html, string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(html) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        try
        {
            var browsingConfig = Configuration.Default;
            using var context = BrowsingContext.New(browsingConfig);
            var document = await context.OpenAsync(req => req.Content(html), cancellationToken);

            var priceSelectors = DetectPriceSelectors(document);
            if (priceSelectors.Count == 0)
            {
                logger.LogDebug("No price selectors detected for {Url}", url);
                return null;
            }

            var nameSelectors = DetectNameSelectors(document);
            var imageSelectors = DetectImageSelectors(document);
            var priceRegexPatterns = DetectPriceRegexPatterns(html);

            var domain = ExtractDomain(uri);
            var storeName = ExtractStoreName(document, domain);

            return new AutoCreateStoreResult
            {
                StoreName = storeName,
                Domain = domain,
                Selectors = new StoreSelectorConfig
                {
                    PriceSelectors = priceSelectors.ToArray(),
                    NameSelectors = nameSelectors.ToArray(),
                    ImageSelectors = imageSelectors.ToArray(),
                    PriceRegexPatterns = priceRegexPatterns.Count > 0 ? priceRegexPatterns.ToArray() : null
                }
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error analyzing HTML for auto-store creation from {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Detects working price selectors using strategies in priority order:
    /// 1. OpenGraph meta tags
    /// 2. Schema.org JSON-LD
    /// 3. Common CSS patterns
    /// </summary>
    private static List<string> DetectPriceSelectors(IDocument document)
    {
        var selectors = new List<string>();

        // Strategy 1: OpenGraph meta tags
        if (HasElement(document, "meta[property='product:price:amount']"))
            selectors.Add("meta[property='product:price:amount']|content");
        if (HasElement(document, "meta[property='og:price:amount']"))
            selectors.Add("meta[property='og:price:amount']|content");

        // Strategy 2: Schema.org JSON-LD
        var jsonLdSelectors = ExtractJsonLdPriceSelectors(document);
        selectors.AddRange(jsonLdSelectors);

        // Strategy 3: Common CSS patterns
        var cssSelectors = new[]
        {
            ("[itemprop='price']", "[itemprop='price']|content"),
            ("[itemprop='price']", "[itemprop='price']"),
            ("[data-price]", "[data-price]"),
            (".price", ".price"),
            (".product-price", ".product-price"),
            (".current-price", ".current-price"),
            (".sale-price", ".sale-price"),
            ("#price", "#price"),
            (".price-current", ".price-current"),
            (".price__current", ".price__current")
        };

        foreach (var (query, selector) in cssSelectors)
        {
            if (selectors.Contains(selector)) continue;
            var element = document.QuerySelector(query);
            if (element == null) continue;

            var text = selector.Contains('|')
                ? element.GetAttribute(selector.Split('|')[1])
                : (element.GetAttribute("data-price") ?? element.GetAttribute("content") ?? element.TextContent);

            if (!string.IsNullOrWhiteSpace(text) && PriceParser.Parse(text) != null)
                selectors.Add(selector);
        }

        return selectors;
    }

    /// <summary>
    /// Extracts price selectors from Schema.org JSON-LD product data.
    /// If JSON-LD contains product pricing, we know itemprop selectors should work.
    /// </summary>
    private static List<string> ExtractJsonLdPriceSelectors(IDocument document)
    {
        var selectors = new List<string>();

        var scripts = document.QuerySelectorAll("script[type='application/ld+json']");
        foreach (var script in scripts)
        {
            try
            {
                var json = script.TextContent.Trim();
                if (string.IsNullOrEmpty(json)) continue;

                using var doc = JsonDocument.Parse(json);
                if (ContainsProductType(doc.RootElement))
                {
                    // JSON-LD Product found - structured data selectors should work
                    if (!selectors.Contains("[itemprop='price']|content"))
                        selectors.Add("[itemprop='price']|content");
                    if (!selectors.Contains("[itemprop='price']"))
                        selectors.Add("[itemprop='price']");
                    break;
                }
            }
            catch (JsonException)
            {
                // Skip malformed JSON-LD
            }
        }

        return selectors;
    }

    private static bool ContainsProductType(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("@type", out var type))
            {
                var typeStr = type.GetString();
                if (typeStr is "Product" or "IndividualProduct")
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsProductType(item))
                    return true;
            }
        }

        return false;
    }

    private static List<string> DetectNameSelectors(IDocument document)
    {
        var selectors = new List<string>();

        if (HasElement(document, "meta[property='og:title']"))
            selectors.Add("meta[property='og:title']|content");

        if (HasElementWithText(document, "[itemprop='name']"))
            selectors.Add("[itemprop='name']");

        if (HasElementWithText(document, "h1"))
            selectors.Add("h1");

        return selectors;
    }

    private static List<string> DetectImageSelectors(IDocument document)
    {
        var selectors = new List<string>();

        if (HasElement(document, "meta[property='og:image']"))
            selectors.Add("meta[property='og:image']|content");

        var imageElement = document.QuerySelector("[itemprop='image']");
        if (imageElement != null)
        {
            var src = imageElement.GetAttribute("src") ?? imageElement.GetAttribute("content");
            if (!string.IsNullOrEmpty(src))
                selectors.Add("[itemprop='image']");
        }

        return selectors;
    }

    private static List<string> DetectPriceRegexPatterns(string html)
    {
        var patterns = new List<string>();

        // Check if common price regex patterns match
        if (PriceJsonRegex().IsMatch(html))
            patterns.Add(@"""price""\s?:\s?""([^""]+)""");

        if (PriceHtmlRegex().IsMatch(html))
            patterns.Add(@">\$(\d+(?:\.\d{2})?)<");

        return patterns;
    }

    private static string ExtractDomain(Uri uri) => ScrapeHelpers.NormalizeHost(uri.Host);

    private static string ExtractStoreName(IDocument document, string domain)
    {
        // Try og:site_name first
        var siteName = document.QuerySelector("meta[property='og:site_name']")?.GetAttribute("content")?.Trim();
        if (!string.IsNullOrEmpty(siteName))
            return siteName;

        // Fall back to capitalizing domain
        var parts = domain.Split('.');
        return parts[0].Length > 0
            ? char.ToUpperInvariant(parts[0][0]) + parts[0][1..]
            : domain;
    }

    private static bool HasElement(IDocument document, string selector) =>
        document.QuerySelector(selector) != null;

    private static bool HasElementWithText(IDocument document, string selector)
    {
        var element = document.QuerySelector(selector);
        return element != null && !string.IsNullOrWhiteSpace(element.TextContent);
    }

    [GeneratedRegex(@"""price""\s?:\s?""[^""]+""")]
    private static partial Regex PriceJsonRegex();

    [GeneratedRegex(@">\$\d+(?:\.\d{2})?<")]
    private static partial Regex PriceHtmlRegex();
}
