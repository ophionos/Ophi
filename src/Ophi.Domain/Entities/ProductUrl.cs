using Ophi.Domain.Enums;

namespace Ophi.Domain.Entities;

public class ProductUrl : BaseEntity
{
    public string Url { get; init; } = string.Empty;
    public string? StoreId { get; init; }
    public decimal? CurrentPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime? LastCheckedAt { get; set; }
    public string? LastError { get; set; }
    public int FailureCount { get; set; }

    // URL health monitoring
    public ProductUrlStatus Status { get; set; } = ProductUrlStatus.Active;
    public int SuspiciousCount { get; set; }
    public string? SuspiciousReason { get; set; }
    public bool IsOutOfStock { get; set; }

    /// <summary>
    /// True once the max-failures notification has been sent for the CURRENT failure streak.
    /// Latches the notification so it fires once per streak rather than on every subsequent check,
    /// while still allowing the threshold itself to be compared with <c>&gt;=</c>. Comparing with
    /// <c>==</c> instead silently skipped the notification forever whenever the user lowered their
    /// auto-pause threshold below a URL's existing failure count. Cleared whenever the streak ends
    /// (successful scrape, out-of-stock, or manual resume).
    /// </summary>
    public bool FailureNotified { get; set; }

    /// <summary>
    /// Whether THIS URL's last successful scrape looked like a price anomaly. Product-level
    /// <see cref="Product.HasPriceAnomaly"/> is derived from all of a product's live URLs; without
    /// per-URL state a clean scrape of one URL erased an anomaly flagged on a sibling.
    /// </summary>
    public bool HasPriceAnomaly { get; set; }

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
        LastCheckedAt = now;
        LastError = null;
        FailureCount = 0;
        FailureNotified = false; // Streak over — a future streak must be able to notify again.
        IsOutOfStock = false;
    }

    /// <summary>
    /// Records a scrape failure that counts against the auto-pause budget: increments
    /// <see cref="FailureCount"/>, stores the error, and stamps <see cref="LastCheckedAt"/>.
    /// Use <see cref="RecordTransientFailure"/> for failures that should not count (e.g.,
    /// rate limiting where the next attempt is expected to succeed).
    /// </summary>
    public void RecordFailure(string? error, DateTime now)
    {
        FailureCount++;
        LastError = error;
        LastCheckedAt = now;
    }

    /// <summary>
    /// Records a transient failure (e.g., rate-limited) without incrementing the failure budget.
    /// </summary>
    public void RecordTransientFailure(string? error, DateTime now)
    {
        LastError = error;
        LastCheckedAt = now;
    }

    /// <summary>
    /// Marks the URL as out of stock. Preserves the last-known price (the scrape worked, the page
    /// just reports unavailable). Returns true when the URL was previously in stock so the caller
    /// can decide whether to publish an out-of-stock transition notification.
    /// </summary>
    public bool MarkOutOfStock(DateTime now)
    {
        var wasInStock = !IsOutOfStock;
        IsOutOfStock = true;
        LastError = null;
        FailureCount = 0; // The scrape worked, just OOS — don't budget against failures.
        FailureNotified = false;
        LastCheckedAt = now;
        return wasInStock;
    }

    /// <summary>
    /// Latches the max-failures notification for the current streak so it is not re-sent on every
    /// subsequent check. See <see cref="FailureNotified"/>.
    /// </summary>
    public void MarkFailureNotified() => FailureNotified = true;

    /// <summary>
    /// Records a suspicious scrape: increments <see cref="SuspiciousCount"/>, captures the reason,
    /// and flips <see cref="Status"/> to <see cref="ProductUrlStatus.Suspicious"/>. The caller
    /// decides whether to also pause (via <see cref="Pause"/>) after consulting the threshold.
    /// </summary>
    public void MarkSuspicious(string reason)
    {
        SuspiciousCount++;
        SuspiciousReason = reason;
        Status = ProductUrlStatus.Suspicious;
    }

    /// <summary>
    /// Resets suspicious state to clean and returns <see cref="Status"/> to
    /// <see cref="ProductUrlStatus.Active"/>. Does nothing if the URL is currently paused — a
    /// paused URL must be resumed explicitly via <see cref="Resume"/>.
    /// </summary>
    public void ClearSuspicious()
    {
        if (Status == ProductUrlStatus.Paused) return;
        SuspiciousCount = 0;
        SuspiciousReason = null;
        Status = ProductUrlStatus.Active;
    }

    /// <summary>Pauses the URL so the dispatcher will skip it on future cycles.</summary>
    public void Pause() => Status = ProductUrlStatus.Paused;

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
        LastCheckedAt = null;
    }

    /// <summary>
    /// Resumes a paused or suspicious URL back to <see cref="ProductUrlStatus.Active"/> and
    /// returns its retry budget by zeroing <see cref="FailureCount"/> and <see cref="SuspiciousCount"/>.
    /// The user is signaling "I think this is fixed" — without the reset, one bad scrape would
    /// re-trip the auto-pause threshold.
    /// </summary>
    public void Resume()
    {
        Status = ProductUrlStatus.Active;
        SuspiciousCount = 0;
        SuspiciousReason = null;
        FailureCount = 0;
        FailureNotified = false;
        HasPriceAnomaly = false;
        LastError = null;
    }
}
