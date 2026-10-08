using Ophi.Domain.Enums;

namespace Ophi.Domain.Entities;

public class Notification : BaseEntity
{
    /// <summary>
    /// Maximum length of <see cref="Title"/>, matching the DB column. Single source of truth
    /// shared by the EF configuration and <see cref="BuildTitle"/>.
    /// </summary>
    public const int TitleMaxLength = 200;

    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public NotificationType Type { get; init; }
    public bool IsRead { get; set; }

    /// <summary>
    /// Builds a "<paramref name="label"/>: <paramref name="name"/>" title bounded to
    /// <see cref="TitleMaxLength"/>. Product names run up to 500 chars but the Title column is 200,
    /// so the name portion is truncated with an ellipsis while the label is always kept intact.
    /// Without this, a long product name overflowed the column and rolled back the whole scrape save.
    /// </summary>
    public static string BuildTitle(string label, string? name)
    {
        name ??= string.Empty;
        var full = $"{label}: {name}";
        if (full.Length <= TitleMaxLength) return full;

        var prefix = $"{label}: ";
        // Pathological: the label alone already fills the budget — hard-truncate the whole title.
        if (prefix.Length >= TitleMaxLength - 1) return full[..TitleMaxLength];

        var keep = TitleMaxLength - prefix.Length - 1; // leave one char for the ellipsis
        // Do not cut between the two halves of a surrogate pair (an emoji): a lone high
        // surrogate is invalid UTF-16 and fails to encode.
        if (char.IsHighSurrogate(name[keep - 1])) keep--;
        return prefix + name[..keep] + "…";
    }

    // Foreign keys
    public Guid UserId { get; init; }
    public Guid? ProductId { get; init; }

    // Navigation properties
    public User User { get; init; } = null!;
    public Product? Product { get; init; }
}
