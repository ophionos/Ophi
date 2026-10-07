using Ophi.Infrastructure.Scraping;

namespace Ophi.Worker.Services;

/// <summary>
/// Analyzes scrape results for suspicious patterns such as redirects to different domains
/// or homepage, price anomalies, and soft-404 pages. Pure static class with no dependencies.
/// </summary>
public static class ScrapeHealthAnalyzer
{
    public record AnalysisResult(bool IsSuspicious, string? Reason);

    private static readonly string[] Soft404Patterns =
    [
        "page not found",
        "error 404",
        "404 -",
        "404 error",
        "product not found",
        "item not found",
        "no longer available",
        "this page doesn't exist",
        "we couldn't find",
        "sorry, this page",
        "oops!"
    ];

    /// <summary>
    /// Analyzes a scrape result for suspicious patterns.
    /// </summary>
    /// <param name="originalUrl">The URL that was scraped</param>
    /// <param name="finalUrl">The URL after all redirects (null if unknown)</param>
    /// <param name="previousPrice">The previous price for this URL (null if first scrape)</param>
    /// <param name="newPrice">The newly scraped price</param>
    /// <param name="priceAnomalyThreshold">Threshold for price change to be considered suspicious (0.7 = 70%)</param>
    /// <param name="pageTitle">The page title for soft-404 detection (null if unknown)</param>
    public static AnalysisResult Analyze(
        string originalUrl,
        string? finalUrl,
        decimal? previousPrice,
        decimal newPrice,
        decimal priceAnomalyThreshold = 0.7m,
        string? pageTitle = null)
    {
        // Check soft-404 first (highest confidence for "page gone")
        var soft404Result = AnalyzeSoft404(pageTitle);
        if (soft404Result.IsSuspicious)
            return soft404Result;

        // Check redirect-based suspicion (high confidence)
        var redirectResult = AnalyzeRedirect(originalUrl, finalUrl);
        if (redirectResult.IsSuspicious)
            return redirectResult;

        // Check price anomaly (lower confidence)
        return AnalyzePriceAnomaly(previousPrice, newPrice, priceAnomalyThreshold);
    }

    /// <summary>
    /// Detects soft-404 pages that return HTTP 200 but show "not found" content.
    /// </summary>
    public static AnalysisResult AnalyzeSoft404(string? pageTitle)
    {
        if (string.IsNullOrEmpty(pageTitle))
            return new AnalysisResult(false, null);

        var lowerTitle = pageTitle.ToLowerInvariant();

        foreach (var pattern in Soft404Patterns)
        {
            if (lowerTitle.Contains(pattern))
                return new AnalysisResult(true, $"Soft-404 detected: page title contains '{pattern}'");
        }

        return new AnalysisResult(false, null);
    }

    private static AnalysisResult AnalyzeRedirect(string originalUrl, string? finalUrl)
    {
        if (string.IsNullOrEmpty(finalUrl))
            return new AnalysisResult(false, null);

        if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var originalUri) ||
            !Uri.TryCreate(finalUrl, UriKind.Absolute, out var finalUri))
            return new AnalysisResult(false, null);

        // Different domain → suspicious. Hosts are normalized (lowercased, "www." stripped) because
        // an apex <-> www redirect is the same site: flagging it marked EVERY scrape of such a URL
        // suspicious, and since SuspiciousCount only resets on a clean scrape it climbed until the
        // URL auto-paused, silently dropping a correctly-tracked URL from scraping and pricing.
        if (!string.Equals(ScrapeHelpers.NormalizeHost(originalUri.Host), ScrapeHelpers.NormalizeHost(finalUri.Host), StringComparison.Ordinal))
            return new AnalysisResult(true, $"Redirected to different domain: {finalUri.Host}");

        // Normalize paths for comparison
        var originalPath = originalUri.AbsolutePath.TrimEnd('/');
        var finalPath = finalUri.AbsolutePath.TrimEnd('/');

        // Same path (ignoring query/fragment/trailing slash) → not suspicious
        if (string.Equals(originalPath, finalPath, StringComparison.OrdinalIgnoreCase))
            return new AnalysisResult(false, null);

        // Landed on homepage (root /) → suspicious
        if (string.IsNullOrEmpty(finalPath) || finalPath == "")
            return new AnalysisResult(true, "Redirected to homepage");

        // Deep path → shallow path (e.g., /products/123 → /category) → suspicious
        var originalSegments = originalPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
        var finalSegments = finalPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;

        if (originalSegments >= 2 && finalSegments < originalSegments)
            return new AnalysisResult(true, $"Redirected to shallower path: {finalPath}");

        return new AnalysisResult(false, null);
    }

    public static AnalysisResult AnalyzePriceAnomaly(decimal? previousPrice, decimal newPrice, decimal threshold)
    {
        // Skip if no previous price (first scrape) or previous price is zero
        if (!previousPrice.HasValue || previousPrice.Value == 0)
            return new AnalysisResult(false, null);

        var changeRatio = Math.Abs(newPrice - previousPrice.Value) / previousPrice.Value;

        if (changeRatio > threshold)
            return new AnalysisResult(true,
                $"Price changed by {changeRatio:P0} (from {previousPrice.Value:F2} to {newPrice:F2})");

        return new AnalysisResult(false, null);
    }
}
