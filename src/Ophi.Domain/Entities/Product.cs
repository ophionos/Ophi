using Ophi.Domain.Enums;

namespace Ophi.Domain.Entities;

public class Product : BaseEntity
{
    // Status and the anomaly flag change only through the methods below; the init accessors serve
    // construction (and test arrange) only. EF Core maps each through its _camelCase backing field.
    private ProductStatus _status = ProductStatus.Active;
    private bool _hasPriceAnomaly;

    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal? CurrentPrice { get; set; }
    public decimal? PreviousPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public ProductStatus Status { get => _status; init => _status = value; }
    public bool IsFavourite { get; set; }
    public bool HasPriceAnomaly { get => _hasPriceAnomaly; init => _hasPriceAnomaly = value; }

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
        _hasPriceAnomaly = ProductUrls.Any(pu => pu.Id != excludingUrlId && pu.ContributesPriceAnomaly);

    /// <summary>
    /// The same rule as <see cref="RecomputePriceAnomaly(Guid?)"/> for a caller that has not loaded
    /// <see cref="ProductUrls"/>: it passes the URL it just changed, and whether any OTHER live URL
    /// (not paused) has an anomaly, usually from a query. Call it after the URL's last status change
    /// in the operation, so that a URL paused in that operation no longer counts.
    /// </summary>
    public void RecomputePriceAnomaly(ProductUrl changedUrl, bool otherLiveUrlHasAnomaly) =>
        _hasPriceAnomaly = otherLiveUrlHasAnomaly || changedUrl.ContributesPriceAnomaly;

    /// <summary>
    /// Takes <paramref name="currency"/> as the product's denomination, but only while the product
    /// has no price. Once priced, the currency changes only through
    /// <see cref="Services.ProductPriceAggregator"/>, together with the price it labels — relabelling
    /// a kept price would also move every alert on the product into or out of dormancy.
    /// </summary>
    public void AdoptCurrencyWhileUnpriced(string? currency)
    {
        if (CurrentPrice is null && currency is not null)
            Currency = currency;
    }

    /// <summary>
    /// Marks the product as <see cref="ProductStatus.Active"/>. Used on the
    /// <c>Pending → Active</c> transition once the first successful scrape lands, and on
    /// resume from <see cref="ProductStatus.Paused"/>. No-op if already Active.
    /// </summary>
    public void MarkActive() => _status = ProductStatus.Active;

    /// <summary>
    /// Marks the product as <see cref="ProductStatus.Error"/>. Callers gate this on the
    /// cross-URL invariant (e.g. "all URLs have hit the failure threshold") before invoking.
    /// </summary>
    public void MarkAsError() => _status = ProductStatus.Error;

    /// <summary>
    /// Pauses the product so the dispatcher skips its URLs entirely on future cycles.
    /// </summary>
    public void Pause() => _status = ProductStatus.Paused;

    /// <summary>
    /// Resumes a paused product back to <see cref="ProductStatus.Active"/>. Equivalent to
    /// <see cref="MarkActive"/> but named for symmetry with <see cref="Pause"/>.
    /// </summary>
    public void Resume() => MarkActive();
}
