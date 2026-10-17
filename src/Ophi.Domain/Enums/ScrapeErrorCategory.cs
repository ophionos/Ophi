namespace Ophi.Domain.Enums;

/// <summary>
/// Categorizes scraping errors to enable differentiated handling
/// (e.g., rate-limited vs. page not found vs. out of stock).
/// Stored as an integer in <c>ProductUrls.LastErrorCategory</c>: the values are explicit so a new
/// member cannot re-label stored rows. Add new members at the end.
/// </summary>
public enum ScrapeErrorCategory
{
    None = 0,
    NotFound = 1,
    Forbidden = 2,
    RateLimited = 3,
    ServerError = 4,
    NetworkError = 5,
    ParseError = 6,
    OutOfStock = 7,
    AntiBot = 8,
    Unknown = 9,

    /// <summary>
    /// The host resolves only to private or reserved addresses (SSRF guard). Permanent, and never
    /// retried through the Playwright fallback.
    /// </summary>
    BlockedDestination = 10
}
