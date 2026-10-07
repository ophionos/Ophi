namespace Ophi.Domain.Entities;

public class ComparisonGroup : BaseEntity
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    // Foreign key
    public Guid UserId { get; init; }

    // Navigation properties
    public User User { get; init; } = null!;
    public ICollection<Product> Products { get; init; } = [];
}
