namespace Ophi.Domain.Entities;

public class StoreConfiguration : BaseEntity
{
    public string StoreId { get; init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DomainPatternsJson { get; set; } = "[]";
    public string SelectorsJson { get; set; } = "{}";
    public string PriceLocale { get; set; } = "en-US";
    public bool RequiresJavaScript { get; set; }
    public bool IsAutoCreated { get; init; }
    public string? CurrencyOverride { get; set; }
    public string? AffiliateParamName { get; set; }
    public string? AffiliateTag { get; set; }
    public string? CustomUserAgent { get; set; }

    // Foreign key
    public Guid UserId { get; init; }

    // Navigation
    public User User { get; init; } = null!;
}
