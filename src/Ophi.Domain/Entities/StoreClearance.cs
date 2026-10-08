namespace Ophi.Domain.Entities;

/// <summary>
/// A browser storage state (cookies) saved after a user solved a store's anti-bot challenge in a
/// remote browser session. Browser scrapes of that host for that user start from it, with the same
/// User-Agent, because clearance cookies are tied to the browser that earned them. Per user, never
/// shared: the remote page accepts typing, so the state can hold the user's own store login. Never
/// returned by the API and never put in a backup.
/// </summary>
public class StoreClearance : BaseEntity
{
    /// <summary>Upper bound; the store's own cookie expiry is usually shorter.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public Guid UserId { get; init; }

    /// <summary>Lower-case host of the URL the challenge was solved on.</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>Playwright storage state JSON.</summary>
    public string StorageState { get; init; } = string.Empty;

    public string UserAgent { get; init; } = string.Empty;

    public DateTime ExpiresAt { get; init; }

    public User User { get; init; } = null!;

    public static StoreClearance Create(Guid userId, string host, string storageState, string userAgent, DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Host = host.ToLowerInvariant(),
            StorageState = storageState,
            UserAgent = userAgent,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now + Lifetime
        };

    public bool IsValidAt(DateTime now) => now < ExpiresAt;
}
