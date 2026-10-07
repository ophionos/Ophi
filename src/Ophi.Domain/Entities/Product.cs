using Ophi.Domain.Enums;

namespace Ophi.Domain.Entities;

public class Product : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal? CurrentPrice { get; set; }
    public decimal? PreviousPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public ProductStatus Status { get; set; } = ProductStatus.Active;
    public bool IsFavourite { get; set; }
    public bool HasPriceAnomaly { get; set; }

    // Check interval in minutes (null = use user's default or system default of 60)
    public int? CheckIntervalMinutes { get; set; }

    // Foreign keys
    public Guid UserId { get; set; }
    public Guid? ComparisonGroupId { get; set; }

    // Navigation properties
    public User User { get; init; } = null!;
    public ComparisonGroup? ComparisonGroup { get; init; }
    public ICollection<ProductUrl> ProductUrls { get; init; } = [];
    public ICollection<PricePoint> PriceHistory { get; init; } = [];
    public ICollection<Alert> Alerts { get; init; } = [];
    public ICollection<ProductTag> ProductTags { get; init; } = [];
    public ICollection<ScrapeLog> ScrapeLogs { get; init; } = [];
    public List<CustomField> CustomFields { get; set; } = [];

    /// <summary>
    /// Recomputes <see cref="HasPriceAnomaly"/> from this product's live URLs. The product flag is
    /// derived from per-URL <see cref="ProductUrl.HasPriceAnomaly"/>, and only the scrape handler
    /// recomputes it — so any path that changes a URL's anomaly contribution outside a scrape
    /// (resume, forced retry, URL removal) must call this, or the flag stays stale until the next
    /// successful scrape lands, which is at least one check interval away.
    /// Paused URLs are excluded, matching how they are excluded from price aggregation.
    /// Requires <see cref="ProductUrls"/> to be loaded; pass <paramref name="excludingUrlId"/> for a
    /// URL being deleted, since EF keeps it in the navigation collection until save.
    /// </summary>
    public void RecomputePriceAnomaly(Guid? excludingUrlId = null) =>
        HasPriceAnomaly = ProductUrls.Any(pu =>
            pu.Id != excludingUrlId
            && pu.Status != ProductUrlStatus.Paused
            && pu.HasPriceAnomaly);

    /// <summary>
    /// Marks the product as <see cref="ProductStatus.Active"/>. Used on the
    /// <c>Pending → Active</c> transition once the first successful scrape lands, and on
    /// resume from <see cref="ProductStatus.Paused"/>. No-op if already Active.
    /// </summary>
    public void MarkActive() => Status = ProductStatus.Active;

    /// <summary>
    /// Marks the product as <see cref="ProductStatus.Error"/>. Callers gate this on the
    /// cross-URL invariant (e.g. "all URLs have hit the failure threshold") before invoking.
    /// </summary>
    public void MarkAsError() => Status = ProductStatus.Error;

    /// <summary>
    /// Pauses the product so the dispatcher skips its URLs entirely on future cycles.
    /// </summary>
    public void Pause() => Status = ProductStatus.Paused;

    /// <summary>
    /// Resumes a paused product back to <see cref="ProductStatus.Active"/>. Equivalent to
    /// <see cref="MarkActive"/> but named for symmetry with <see cref="Pause"/>.
    /// </summary>
    public void Resume() => Status = ProductStatus.Active;
}
