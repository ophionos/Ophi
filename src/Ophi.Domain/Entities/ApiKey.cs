namespace Ophi.Domain.Entities;

public class ApiKey : BaseEntity
{
    public Guid UserId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string KeyHash { get; init; } = string.Empty;
    public List<string> Scopes { get; set; } = [];
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    // Navigation properties
    public User User { get; init; } = null!;
}
