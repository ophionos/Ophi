namespace Ophi.Domain.Entities;

public class BasketItem
{
    private int _quantity = 1;

    // Composite key (BasketId, ProductId)
    public Guid BasketId { get; init; }
    public Guid ProductId { get; init; }

    /// <summary>At least 1. Changed through <see cref="Basket.SetQuantity"/>.</summary>
    public int Quantity { get => _quantity; init => _quantity = value; }

    public DateTime AddedAt { get; init; }

    // Navigation properties
    public Basket Basket { get; init; } = null!;
    public Product Product { get; init; } = null!;

    internal void SetQuantity(int quantity) => _quantity = quantity;
}
