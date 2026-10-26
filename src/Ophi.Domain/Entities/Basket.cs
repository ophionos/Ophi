namespace Ophi.Domain.Entities;

/// <summary>
/// Products a user buys together, each with a quantity. Not a mode of <see cref="ComparisonGroup"/>:
/// a group holds alternatives for one need, a basket holds distinct items. A product may sit in
/// several baskets. Changes go through the methods, which keep one item per product.
/// </summary>
public class Basket : BaseEntity
{
    private string _name = string.Empty;
    private string? _description;

    public string Name { get => _name; init => _name = value; }
    public string? Description { get => _description; init => _description = value; }

    // Foreign key
    public Guid UserId { get; init; }

    // Navigation properties
    public User User { get; init; } = null!;
    public ICollection<BasketItem> Items { get; init; } = [];

    /// <summary>Adds the product, or returns the existing item unchanged (its quantity is kept).</summary>
    public BasketItem AddItem(Guid productId, int quantity = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);

        var existing = Items.FirstOrDefault(i => i.ProductId == productId);
        if (existing != null) return existing;

        var item = new BasketItem
        {
            BasketId = Id,
            ProductId = productId,
            Quantity = quantity,
            AddedAt = DateTime.UtcNow
        };
        Items.Add(item);
        return item;
    }

    public void SetQuantity(Guid productId, int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);

        var item = Items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new InvalidOperationException("The product is not in this basket.");
        item.SetQuantity(quantity);
    }

    /// <summary>Returns false when the product is not in the basket.</summary>
    public bool RemoveItem(Guid productId)
    {
        var item = Items.FirstOrDefault(i => i.ProductId == productId);
        return item != null && Items.Remove(item);
    }

    public void Rename(string name, string? description)
    {
        _name = name;
        _description = description;
    }
}
