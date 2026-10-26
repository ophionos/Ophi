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
                // Information, not Debug: this is the only trace of why a store config was not created.
                logger.LogInformation("No price selectors detected for {Url}; no store configuration created", url);
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
    /// 2. Schema.org microdata
    /// 3. The JSON-LD Product offer price
    /// 4. Common CSS patterns
    /// The same order as <see cref="CommonSelectors.PriceSelectors"/>, so an auto-created store does
    /// not put <c>.price</c> (which can match a related-product card) ahead of JSON-LD.
    /// </summary>
    private static List<string> DetectPriceSelectors(IDocument document)
    {
        var selectors = new List<string>();

        // Strategy 1: OpenGraph meta tags
        if (HasElement(document, "meta[property='product:price:amount']"))
            selectors.Add("meta[property='product:price:amount']|content");
        if (HasElement(document, "meta[property='og:price:amount']"))
            selectors.Add("meta[property='og:price:amount']|content");

        // Strategy 2: Schema.org microdata
        AddParsingSelectors(document, selectors,
        [
            ("[itemprop='price']", "[itemprop='price']|content"),
            ("[itemprop='price']", "[itemprop='price']")
        ]);

        // Strategy 3: Schema.org JSON-LD
        if (JsonPathExtractor.ExtractProductOfferPrices(JsonPathExtractor.GetJsonLdContents(document))
            .Any(p => PriceParser.Parse(p) != null))
            selectors.Add(CommonSelectors.JsonLdProductPrice);

        // Strategy 4: Common CSS patterns
        AddParsingSelectors(document, selectors,
        [
            ("[data-price]", "[data-price]"),
            (".price ins .woocommerce-Price-amount", ".price ins .woocommerce-Price-amount"),
            (".price > .woocommerce-Price-amount", ".price > .woocommerce-Price-amount"),
            (".price", ".price"),
            (".product-price", ".product-price"),
            (".current-price", ".current-price"),
            (".sale-price", ".sale-price"),
            ("#price", "#price"),
            (".price-current", ".price-current"),
            (".price__current", ".price__current")
        ]);

        return selectors;
    }

    /// <summary>
    /// Adds each selector whose first match on the page parses as a price.
    /// </summary>
    private static void AddParsingSelectors(IDocument document, List<string> selectors, (string Query, string Selector)[] candidates)
    {
        foreach (var (query, selector) in candidates)
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
