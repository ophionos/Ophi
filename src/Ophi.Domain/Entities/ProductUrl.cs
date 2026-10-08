using Ophi.Domain.Enums;

namespace Ophi.Domain.Entities;

public class ProductUrl : BaseEntity
{
    // Scrape-health state changes only through the methods below; the init accessors serve
    // construction (and test arrange) only. EF Core maps each property through its _camelCase
    // backing field by convention.
    private DateTime? _lastCheckedAt;
    private string? _lastError;
    private int _failureCount;
    private ProductUrlStatus _status = ProductUrlStatus.Active;
    private int _suspiciousCount;
    private string? _suspiciousReason;
    private bool _isOutOfStock;
    private bool _failureNotified;
    private bool _hasPriceAnomaly;
    private int _consecutiveFailures;
    private DateTime? _backoffUntil;

    /// <summary>The longest wait <see cref="BackoffUntil"/> imposes between checks.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(24);

    public string Url { get; init; } = string.Empty;
    public string? StoreId { get; init; }
    public decimal? CurrentPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime? LastCheckedAt { get => _lastCheckedAt; init => _lastCheckedAt = value; }
    public string? LastError { get => _lastError; init => _lastError = value; }
    public int FailureCount { get => _failureCount; init => _failureCount = value; }

    // URL health monitoring
    public ProductUrlStatus Status { get => _status; init => _status = value; }
    public int SuspiciousCount { get => _suspiciousCount; init => _suspiciousCount = value; }
    public string? SuspiciousReason { get => _suspiciousReason; init => _suspiciousReason = value; }
    public bool IsOutOfStock { get => _isOutOfStock; init => _isOutOfStock = value; }

    /// <summary>
    /// True once the max-failures notification has been sent for the CURRENT failure streak.
    /// Latches the notification so it fires once per streak rather than on every subsequent check,
    /// while still allowing the threshold itself to be compared with <c>&gt;=</c>. Comparing with
    /// <c>==</c> instead silently skipped the notification forever whenever the user lowered their
    /// auto-pause threshold below a URL's existing failure count. Cleared whenever the streak ends
    /// (successful scrape, out-of-stock, or manual resume).
    /// </summary>
    public bool FailureNotified { get => _failureNotified; init => _failureNotified = value; }

    /// <summary>
    /// Whether THIS URL's last successful scrape looked like a price anomaly. Product-level
    /// <see cref="Product.HasPriceAnomaly"/> is derived from all of a product's live URLs; without
    /// per-URL state a clean scrape of one URL erased an anomaly flagged on a sibling.
    /// </summary>
    public bool HasPriceAnomaly { get => _hasPriceAnomaly; init => _hasPriceAnomaly = value; }

    /// <summary>
    /// Failed checks in a row, of any category. Unlike <see cref="FailureCount"/> (the auto-pause
    /// budget), it also counts rate-limited checks, so a URL that only ever gets 429s still backs off.
    /// </summary>
    public int ConsecutiveFailures { get => _consecutiveFailures; init => _consecutiveFailures = value; }

    /// <summary>
    /// The dispatcher does not check this URL before this time, whatever its check interval.
    /// Set from the second consecutive failure on: 2h, then doubling, capped at <see cref="MaxBackoff"/>.
    /// Cleared whenever the failure streak ends.
    /// </summary>
    public DateTime? BackoffUntil { get => _backoffUntil; init => _backoffUntil = value; }

    /// <summary>
    /// Whether this URL feeds <see cref="Product.HasPriceAnomaly"/>: paused URLs are excluded, as
    /// they are from price aggregation. Not translatable to SQL — a query must restate it.
    /// </summary>
    public bool ContributesPriceAnomaly => Status != ProductUrlStatus.Paused && HasPriceAnomaly;

    // Selector configuration
    public string? Selector { get; set; }
    public SelectorType SelectorType { get; init; } = SelectorType.Auto;

    // Foreign key
    public Guid ProductId { get; init; }

    // Navigation properties
    public Product Product { get; init; } = null!;
    public ICollection<PricePoint> PriceHistory { get; init; } = [];

    /// <summary>
    /// Applies a successful scrape result: updates price/currency, stamps <see cref="LastCheckedAt"/>,
    /// clears <see cref="LastError"/> and <see cref="FailureCount"/>, and clears the out-of-stock flag.
    /// Does NOT touch suspicious state — callers run health analysis separately and invoke
    /// <see cref="MarkSuspicious"/> / <see cref="ClearSuspicious"/> as needed.
    /// </summary>
    public void RecordSuccessfulScrape(decimal price, string? currency, DateTime now)
    {
        CurrentPrice = price;
        Currency = currency ?? Currency;
        _lastCheckedAt = now;
        _lastError = null;
        _failureCount = 0;
        _failureNotified = false; // Streak over — a future streak must be able to notify again.
        _isOutOfStock = false;
        ClearBackoff();
    }

    /// <summary>
    /// Records a scrape failure that counts against the auto-pause budget: increments
    /// <see cref="FailureCount"/>, stores the error, and stamps <see cref="LastCheckedAt"/>.
    /// Use <see cref="RecordTransientFailure"/> for failures that should not count (e.g.,
    /// rate limiting where the next attempt is expected to succeed).
    /// </summary>
    public void RecordFailure(string? error, DateTime now)
    {
        _failureCount++;
        _lastError = error;
        _lastCheckedAt = now;
        ExtendBackoff(now);
    }

    /// <summary>
    /// Records whether this URL's latest scraped price looked like an anomaly. The product flag is
    /// derived from it; recompute that with <see cref="Product.RecomputePriceAnomaly(ProductUrl, bool)"/>.
    /// </summary>
    public void RecordPriceAnomaly(bool isAnomalous) => _hasPriceAnomaly = isAnomalous;

    /// <summary>
    /// Stamps <see cref="LastCheckedAt"/> for a check whose result was deliberately not applied
    /// (a suspicious scrape at the auto-pause threshold), so the URL still reads as checked.
    /// </summary>
    public void MarkChecked(DateTime now) => _lastCheckedAt = now;

    /// <summary>
    /// Records a transient failure (e.g., rate-limited) without incrementing the failure budget.
    /// </summary>
    public void RecordTransientFailure(string? error, DateTime now)
    {
        _lastError = error;
        _lastCheckedAt = now;
        ExtendBackoff(now);
    }

    private void ExtendBackoff(DateTime now)
    {
        _consecutiveFailures++;
        if (_consecutiveFailures < 2)
            return;

        var hours = Math.Pow(2, Math.Min(_consecutiveFailures - 1, 5));
        _backoffUntil = now + TimeSpan.FromHours(Math.Min(hours, MaxBackoff.TotalHours));
    }

    private void ClearBackoff()
    {
        _consecutiveFailures = 0;
        _backoffUntil = null;
    }

    /// <summary>
    /// Marks the URL as out of stock. Preserves the last-known price (the scrape worked, the page
    /// just reports unavailable). Returns true when the URL was previously in stock so the caller
    /// can decide whether to publish an out-of-stock transition notification.
    /// <paramref name="scrapedCurrency"/> is adopted only while the URL has no price: a preserved
    /// price keeps the denomination it was scraped in.
    /// </summary>
    public bool MarkOutOfStock(DateTime now, string? scrapedCurrency = null)
    {
        if (CurrentPrice is null && scrapedCurrency is not null)
            Currency = scrapedCurrency;

        var wasInStock = !_isOutOfStock;
        _isOutOfStock = true;
        _lastError = null;
        _failureCount = 0; // The scrape worked, just OOS — don't budget against failures.
        _failureNotified = false;
        _lastCheckedAt = now;
        ClearBackoff();
        return wasInStock;
    }

    /// <summary>
    /// Latches the max-failures notification for the current streak so it is not re-sent on every
    /// subsequent check. See <see cref="FailureNotified"/>.
    /// </summary>
    public void MarkFailureNotified() => _failureNotified = true;

    /// <summary>
    /// Records a suspicious scrape: increments <see cref="SuspiciousCount"/>, captures the reason,
    /// and flips <see cref="Status"/> to <see cref="ProductUrlStatus.Suspicious"/>. The caller
    /// decides whether to also pause (via <see cref="Pause"/>) after consulting the threshold.
    /// A paused URL stays paused, as in <see cref="ClearSuspicious"/>: only <see cref="Resume"/>
    /// leaves <see cref="ProductUrlStatus.Paused"/>.
    /// </summary>
    public void MarkSuspicious(string reason)
    {
        _suspiciousCount++;
        _suspiciousReason = reason;
        if (_status != ProductUrlStatus.Paused)
            _status = ProductUrlStatus.Suspicious;
    }

    /// <summary>
    /// Resets suspicious state to clean and returns <see cref="Status"/> to
    /// <see cref="ProductUrlStatus.Active"/>. Does nothing if the URL is currently paused — a
    /// paused URL must be resumed explicitly via <see cref="Resume"/>.
    /// </summary>
    public void ClearSuspicious()
    {
        if (_status == ProductUrlStatus.Paused) return;
        _suspiciousCount = 0;
        _suspiciousReason = null;
        _status = ProductUrlStatus.Active;
    }

    /// <summary>Pauses the URL so the dispatcher will skip it on future cycles.</summary>
    public void Pause() => _status = ProductUrlStatus.Paused;

    /// <summary>
    /// Marks the URL for an immediate re-scrape on the dispatcher's next cycle, bypassing the
    /// check-interval and cache-TTL gates. Used by the manual "retry" action: <see cref="Resume"/>s
    /// the URL (back to Active, clearing the failure/suspicious budget and error) and nulls
    /// <see cref="LastCheckedAt"/> so the dispatcher's due-check short-circuits to true. The API
    /// and worker share only the database, so this DB signal is how an on-demand retry reaches the
    /// worker — publishing a message cross-process would be dropped as "no routes".
    /// </summary>
    public void RequestImmediateRescrape()
    {
        Resume();
        _lastCheckedAt = null;
    }

    /// <summary>
    /// Resumes a paused or suspicious URL back to <see cref="ProductUrlStatus.Active"/> and
    /// returns its retry budget by zeroing <see cref="FailureCount"/> and <see cref="SuspiciousCount"/>.
    /// The user is signaling "I think this is fixed" — without the reset, one bad scrape would
    /// re-trip the auto-pause threshold.
    /// </summary>
    public void Resume()
    {
        _status = ProductUrlStatus.Active;
        _suspiciousCount = 0;
        _suspiciousReason = null;
        _failureCount = 0;
        _failureNotified = false;
        _hasPriceAnomaly = false;
        _lastError = null;
        ClearBackoff();
    }
}
