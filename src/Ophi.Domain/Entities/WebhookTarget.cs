namespace Ophi.Domain.Entities;

public class WebhookTarget : BaseEntity
{
    public Guid UserId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public List<string> Events { get; set; } = [];
    public bool IsEnabled { get; set; } = true;

    // Navigation properties
    public User User { get; init; } = null!;
}
