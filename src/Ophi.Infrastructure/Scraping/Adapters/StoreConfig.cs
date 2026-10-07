namespace Ophi.Infrastructure.Scraping.Adapters;

/// <summary>
/// Configuration for a specific e-commerce store.
/// Serializable for future database storage.
/// </summary>
public record StoreConfig
{
    /// <summary>
    /// Unique identifier for this store (e.g., "amazon", "ebay").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display name for the store (e.g., "Amazon", "eBay").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Domain patterns to match against URLs (e.g., ["amazon.com", "amazon.co.uk"]).
    /// Matching is case-insensitive and checks if the URL host ends with the pattern.
    /// </summary>
    public required string[] DomainPatterns { get; init; }

    /// <summary>
    /// Selector configuration for this store.
    /// </summary>
    public required StoreSelectorConfig Selectors { get; init; }

    /// <summary>
    /// Whether this is a built-in store configuration.
    /// User-defined stores will have this set to false.
    /// </summary>
    public bool IsBuiltIn { get; init; } = true;

    /// <summary>
    /// Database entity ID for user-defined stores (null for built-in stores).
    /// </summary>
    public Guid? EntityId { get; init; }

    /// <summary>
    /// Creation timestamp for user-defined stores (null for built-in stores).
    /// </summary>
    public DateTime? CreatedAt { get; init; }

    /// <summary>
    /// Whether this store requires JavaScript rendering (Playwright) for scraping.
    /// Sites like Amazon that heavily rely on JS for rendering product data should have this set to true.
    /// </summary>
    public bool RequiresJavaScript { get; init; }

    /// <summary>
    /// Whether this store configuration was automatically created by analyzing page HTML.
    /// </summary>
    public bool IsAutoCreated { get; init; }

    /// <summary>
    /// BCP 47 / .NET culture name for parsing prices (e.g., "en-US", "pt-PT", "de-DE").
    /// Determines how decimal and thousands separators are interpreted.
    /// </summary>
    public string PriceLocale { get; init; } = "en-US";

    /// <summary>
    /// Optional ISO 4217 currency code override (e.g., "EUR", "USD").
    /// When set, overrides any currency detected by the scraper.
    /// </summary>
    public string? CurrencyOverride { get; init; }

    /// <summary>
    /// Query parameter name for affiliate link injection (e.g., "tag" for Amazon).
    /// </summary>
    public string? AffiliateParamName { get; init; }

    /// <summary>
    /// Affiliate code/tag value (e.g., "ophi-20").
    /// </summary>
    public string? AffiliateTag { get; init; }

    /// <summary>
    /// Optional custom User-Agent string for this store.
    /// When set, overrides the automatic browser profile rotation for requests to this store.
    /// </summary>
    public string? CustomUserAgent { get; init; }
}
