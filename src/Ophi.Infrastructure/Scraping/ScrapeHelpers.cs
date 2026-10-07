using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Ophi.Infrastructure.Scraping;

/// <summary>
/// Shared helpers used by both <see cref="ScrapingService"/> and <see cref="PlaywrightScrapingService"/>.
/// </summary>
internal static class ScrapeHelpers
{
    /// <summary>
    /// Bound on a single regex match. <c>PriceRegexPatterns</c> are user-authored (via
    /// CreateStore/UpdateStore/ImportStore) and are matched against a whole page — a
    /// catastrophically-backtracking pattern would otherwise pin a worker thread indefinitely.
    /// </summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();

    /// <summary>
    /// Compiles and caches a user-supplied regex pattern with <see cref="RegexTimeout"/> applied.
    /// Lives here rather than on each service because the two had drifted: the HTTP scraper passed
    /// a timeout and the Playwright one did not, which made the latter's
    /// <see cref="RegexMatchTimeoutException"/> handlers unreachable.
    /// </summary>
    public static Regex GetOrCreateRegex(string pattern) =>
        RegexCache.GetOrAdd(pattern, p => new Regex(p, RegexOptions.Compiled, RegexTimeout));

    /// <summary>
    /// Parses a selector specification into selector and optional attribute name.
    /// Format: "selector" or "selector|attribute"
    /// </summary>
    public static (string Selector, string? AttributeName) ParseSelectorSpec(string selectorSpec)
    {
        var pipeIndex = selectorSpec.IndexOf('|');
        return pipeIndex > 0 ? (selectorSpec[..pipeIndex], selectorSpec[(pipeIndex + 1)..]) : (selectorSpec, null);
    }

    /// <summary>
    /// Converts relative URLs to absolute URLs.
    /// </summary>
    public static string NormalizeImageUrl(string src, string baseUrl)
    {
        if (src.StartsWith("//", StringComparison.Ordinal))
            return "https:" + src;

        if (src.StartsWith('/'))
        {
            var uri = new Uri(baseUrl);
            return $"{uri.Scheme}://{uri.Host}{src}";
        }

        return src;
    }

    /// <summary>
    /// Normalizes a URI host by lowercasing and stripping the "www." prefix.
    /// </summary>
    public static string NormalizeHost(string host)
    {
        host = host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
            host = host[4..];
        return host;
    }
}
