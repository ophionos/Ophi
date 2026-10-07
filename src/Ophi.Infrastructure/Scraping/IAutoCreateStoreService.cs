using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Infrastructure.Scraping;

/// <summary>
/// Analyzes HTML content to auto-detect working selectors for unknown stores.
/// </summary>
public interface IAutoCreateStoreService
{
    /// <summary>
    /// Analyzes HTML content and attempts to detect selectors for price, name, and image extraction.
    /// Returns null if no viable selectors could be detected.
    /// </summary>
    Task<AutoCreateStoreResult?> AnalyzeHtmlAsync(string html, string url, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of auto-detecting store selectors from HTML content.
/// </summary>
public record AutoCreateStoreResult
{
    public required string StoreName { get; init; }
    public required string Domain { get; init; }
    public required StoreSelectorConfig Selectors { get; init; }
}
