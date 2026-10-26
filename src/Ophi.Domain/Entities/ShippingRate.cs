namespace Ophi.Domain.Entities;

/// <summary>
/// A user's flat shipping cost for one site, keyed by the normalized host (lowercase, no "www."),
/// not by <see cref="StoreConfiguration.StoreId"/>: one store id can span regional sites with
/// different shipping and currencies. Read through <see cref="Services.LandedPrice"/>.
/// </summary>
public class ShippingRate : BaseEntity
{
    private decimal _amount;
    private string _currency = string.Empty;
    private decimal? _freeAbove;

    public string Host { get; init; } = string.Empty;

    public decimal Amount { get => _amount; init => _amount = value; }

    /// <summary>ISO code. Shipping only applies to prices in this currency; it is never converted.</summary>
    public string Currency { get => _currency; init => _currency = value; }

    /// <summary>When set, an order of at least this amount ships free.</summary>
    public decimal? FreeAbove { get => _freeAbove; init => _freeAbove = value; }

    // Foreign key
    public Guid UserId { get; init; }

    // Navigation
    public User User { get; init; } = null!;

    public void Update(decimal amount, string currency, decimal? freeAbove)
    {
        _amount = amount;
        _currency = currency;
        _freeAbove = freeAbove;
    }
}
