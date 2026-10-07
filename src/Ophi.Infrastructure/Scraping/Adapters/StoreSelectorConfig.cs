namespace Ophi.Infrastructure.Scraping.Adapters;

/// <summary>
/// Configuration for store-specific selectors.
/// Uses string arrays for serialization compatibility (DB storage, JSON).
/// </summary>
public record StoreSelectorConfig
{
    /// <summary>
    /// Price selectors in order of priority. Format: "selector" or "selector|attribute"
    /// </summary>
    public required string[] PriceSelectors { get; init; }

    /// <summary>
    /// Name selectors in order of priority.
    /// </summary>
    public required string[] NameSelectors { get; init; }

    /// <summary>
    /// Image selectors in order of priority.
    /// </summary>
    public required string[] ImageSelectors { get; init; }

    /// <summary>
    /// Optional regex patterns for price extraction (fallback).
    /// Stored as strings for serialization; compiled at runtime.
    /// </summary>
    public string[]? PriceRegexPatterns { get; init; }

    /// <summary>
    /// Optional regex patterns for image extraction (fallback).
    /// Stored as strings for serialization; compiled at runtime.
    /// </summary>
    public string[]? ImageRegexPatterns { get; init; }

    /// <summary>
    /// Optional JSONPath expressions for price extraction from JSON-LD blocks.
    /// Applied after CSS selectors and before regex patterns.
    /// </summary>
    public string[]? PriceJsonPaths { get; init; }

    /// <summary>
    /// Optional JSONPath expressions for name extraction from JSON-LD blocks.
    /// </summary>
    public string[]? NameJsonPaths { get; init; }

    /// <summary>
    /// Optional JSONPath expressions for image URL extraction from JSON-LD blocks.
    /// </summary>
    public string[]? ImageJsonPaths { get; init; }

    /// <summary>
    /// Optional CSS selectors for out-of-stock detection.
    /// Falls back to CommonSelectors.OutOfStockSelectors when null.
    /// </summary>
    public string[]? OutOfStockSelectors { get; init; }

    /// <summary>
    /// Optional text patterns that indicate a product is out of stock.
    /// Falls back to CommonSelectors.OutOfStockTextPatterns when null.
    /// </summary>
    public string[]? OutOfStockTextPatterns { get; init; }

    /// <summary>
    /// Optional text patterns indicating no purchasable offer exists for the viewer's locale
    /// (e.g. a geo shipping restriction). When matched anywhere in the page body, any extracted
    /// price is discarded and the listing is recorded as unavailable. Falls back to
    /// CommonSelectors.UnavailableTextPatterns when null.
    /// </summary>
    public string[]? UnavailableTextPatterns { get; init; }
}
