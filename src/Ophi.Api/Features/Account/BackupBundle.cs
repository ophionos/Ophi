using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ophi.Api.Features.Account;

/// <summary>
/// The account backup file (B-1). Shared by <see cref="ExportBackup"/> and <see cref="ImportBackup"/>.
/// <para>
/// Members added after version 1 (<c>ShippingRates</c>, <c>Baskets</c>, <c>Settings.ShowLandedPrices</c>,
/// <c>BackupProduct.Ref</c>) default when
/// the file lacks them, so older backups still import without a version bump.
/// </para>
/// <para>
/// IDs in the file (<c>Ref</c> fields) are references inside the bundle only — import always assigns
/// new IDs. Credentials, and anything that works as one, are never written: see
/// <see cref="Excluded"/>.
/// </para>
/// </summary>
public record BackupBundle(
    string Format,
    int Version,
    DateTime ExportedAt,
    IReadOnlyList<string> Excluded,
    BackupSettings Settings,
    IReadOnlyList<BackupTag> Tags,
    IReadOnlyList<BackupComparisonGroup> ComparisonGroups,
    IReadOnlyList<BackupStore> Stores,
    IReadOnlyList<BackupProduct> Products,
    IReadOnlyList<BackupShippingRate>? ShippingRates = null,
    IReadOnlyList<BackupBasket>? Baskets = null)
{
    public const string FormatName = "ophi-backup";
    public const int CurrentVersion = 1;

    /// <summary>What the file deliberately leaves out, so a user knows what to re-enter after a restore.</summary>
    public static readonly IReadOnlyList<string> ExcludedItems =
    [
        "password",
        "api keys",
        "discord webhook url",
        "telegram chat id",
        "pushover user key",
        "ntfy topic url",
        "outbound webhooks (their URL is often a credential)",
        "notifications",
        "scrape logs",
        "store clearances (browser cookies from solved anti-bot challenges)"
    ];

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public record BackupSettings(
    bool AffiliatesEnabled,
    int? DefaultCheckIntervalMinutes,
    int? PageFetchDelaySeconds,
    int? ScrapeCacheTtlMinutes,
    int? AnomalyThresholdPercent,
    int? AutoPauseAfterFailures,
    bool EmailNotificationsEnabled,
    string? DisplayCurrency,
    bool ShowLandedPrices = false);

/// <summary>Optional in the file: backups written before shipping rates existed have none.</summary>
public record BackupShippingRate(string Host, decimal Amount, string Currency, decimal? FreeAbove);

/// <summary>Optional in the file: backups written before baskets existed have none, and before basket alerts no <c>Alerts</c>.</summary>
public record BackupBasket(string Name, string? Description, IReadOnlyList<BackupBasketItem> Items,
    IReadOnlyList<BackupBasketAlert>? Alerts = null);

/// <summary><paramref name="Host"/> null watches the best store.</summary>
public record BackupBasketAlert(decimal TargetTotal, string Currency, string? Host, bool Active);

/// <summary><paramref name="ProductRef"/> is the <see cref="BackupProduct.Ref"/> of a product in the same file.</summary>
public record BackupBasketItem(Guid ProductRef, int Quantity);

public record BackupTag(Guid Ref, string Name, string Color, int Weight);

public record BackupComparisonGroup(Guid Ref, string Name, string? Description);

public record BackupStore(
    string StoreId,
    string Name,
    string DomainPatternsJson,
    string SelectorsJson,
    string PriceLocale,
    bool RequiresJavaScript,
    string? CurrencyOverride,
    string? AffiliateParamName,
    string? AffiliateTag,
    string? CustomUserAgent);

public record BackupProduct(
    string Name,
    string? ImageUrl,
    decimal? CurrentPrice,
    decimal? PreviousPrice,
    string Currency,
    string Status,
    bool IsFavourite,
    int? CheckIntervalMinutes,
    IReadOnlyList<BackupCustomField> CustomFields,
    IReadOnlyList<Guid> TagRefs,
    Guid? ComparisonGroupRef,
    IReadOnlyList<BackupProductUrl> Urls,
    IReadOnlyList<BackupPricePoint> PriceHistory,
    IReadOnlyList<BackupAlert> Alerts,
    Guid? Ref = null);

public record BackupCustomField(string Name, string Value);

public record BackupProductUrl(
    Guid Ref,
    string Url,
    string? StoreId,
    decimal? CurrentPrice,
    string Currency,
    DateTime? LastCheckedAt,
    string Status,
    bool IsOutOfStock,
    string? Selector,
    string SelectorType);

public record BackupPricePoint(Guid? UrlRef, decimal Price, string Currency, DateTime RecordedAt);

public record BackupAlert(
    string Condition,
    decimal TargetPrice,
    decimal ReferencePrice,
    string Currency,
    bool Active,
    DateTime? LastTriggeredAt,
    int TriggerCount);
