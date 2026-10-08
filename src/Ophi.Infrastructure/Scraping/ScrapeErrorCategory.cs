namespace Ophi.Infrastructure.Scraping;

/// <summary>
/// Categorizes scraping errors to enable differentiated handling
/// (e.g., rate-limited vs. page not found vs. out of stock).
/// </summary>
public enum ScrapeErrorCategory
{
    None,
    NotFound,
    Forbidden,
    RateLimited,
    ServerError,
    NetworkError,
    ParseError,
    OutOfStock,
    AntiBot,
    Unknown,

    /// <summary>
    /// The host resolves only to private or reserved addresses (SSRF guard). Permanent, and never
    /// retried through the Playwright fallback.
    /// </summary>
    BlockedDestination
}
