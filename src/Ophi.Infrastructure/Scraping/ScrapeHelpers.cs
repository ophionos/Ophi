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

    /// <summary>
    /// Bound on <see cref="RegexCache"/>. Patterns are user-authored, so an edited store config would
    /// otherwise leave its old compiled patterns in memory for the life of the process. Reaching the
    /// bound clears the cache; live patterns are recompiled on their next use.
    /// </summary>
    internal const int MaxCachedRegexes = 1000;

    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();

    internal static int CachedRegexCount => RegexCache.Count;

    /// <summary>
    /// Compiles and caches a user-supplied regex pattern with <see cref="RegexTimeout"/> applied.
    /// Lives here rather than on each service because the two had drifted: the HTTP scraper passed
    /// a timeout and the Playwright one did not, which made the latter's
    /// <see cref="RegexMatchTimeoutException"/> handlers unreachable.
    /// </summary>
    public static Regex GetOrCreateRegex(string pattern)
    {
        if (RegexCache.TryGetValue(pattern, out var cached))
            return cached;
        if (RegexCache.Count >= MaxCachedRegexes)
            RegexCache.Clear();
        return RegexCache.GetOrAdd(pattern, p => new Regex(p, RegexOptions.Compiled, RegexTimeout));
    }

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
    /// Resolves an image <c>src</c> against the page URL the way a browser does, keeping the page's
    /// scheme and port. A value that cannot be resolved is returned unchanged.
    /// </summary>
    public static string NormalizeImageUrl(string src, string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var page) && Uri.TryCreate(page, src, out var resolved)
            ? resolved.AbsoluteUri
            : src;

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
