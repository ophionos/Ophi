namespace Ophi.Domain.Entities;

public class ProductTag
{
    public Guid ProductId { get; init; }
    public Guid TagId { get; init; }

    // Navigation properties
    public Product Product { get; init; } = null!;
    public Tag Tag { get; init; } = null!;
}
