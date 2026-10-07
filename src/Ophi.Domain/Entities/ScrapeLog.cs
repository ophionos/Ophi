namespace Ophi.Domain.Entities;

public class ScrapeLog : BaseEntity
{
    public Guid ProductId { get; init; }
    public Guid? ProductUrlId { get; init; }
    public bool Success { get; init; }
    public decimal? Price { get; init; }
    public string? Error { get; init; }
    public int DurationMs { get; init; }
    public bool IsOutOfStock { get; init; }
    public string StoreDomain { get; init; } = string.Empty;

    // Navigation properties
    public Product Product { get; init; } = null!;
    public ProductUrl? ProductUrl { get; init; }
}
