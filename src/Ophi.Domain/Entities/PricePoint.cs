namespace Ophi.Domain.Entities;

public class PricePoint : BaseEntity
{
    public decimal Price { get; init; }
    public string Currency { get; init; } = "USD";
    public DateTime RecordedAt { get; init; }

    // Foreign keys
    public Guid ProductId { get; init; }
    public Guid? ProductUrlId { get; init; }

    // Navigation properties
    public Product Product { get; init; } = null!;
    public ProductUrl? ProductUrl { get; init; }
}
