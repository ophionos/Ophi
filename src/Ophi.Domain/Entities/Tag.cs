namespace Ophi.Domain.Entities;

public class Tag : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#3B82F6";
    public int Weight { get; set; }

    // Foreign key
    public Guid UserId { get; init; }

    // Navigation properties
    public User User { get; init; } = null!;
    public ICollection<ProductTag> ProductTags { get; init; } = [];
}
