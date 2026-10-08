namespace Ophi.Domain.Entities;

public class User : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool EmailVerified { get; init; }
    public string SecurityStamp { get; set; } = NewSecurityStamp();
    public DateTime? LastLoginAt { get; set; }
    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetTokenExpiresAt { get; set; }
    public bool AffiliatesEnabled { get; set; } = true;
    public int? DefaultCheckIntervalMinutes { get; set; }
    public int? PageFetchDelaySeconds { get; set; }
    public int? ScrapeCacheTtlMinutes { get; set; }
    public string? DiscordWebhookUrl { get; set; }
    public bool DiscordNotificationsEnabled { get; set; }

    /// <summary>Where the operator's Telegram bot sends this user's alerts. Addresses a chat; not a credential.</summary>
    public string? TelegramChatId { get; set; }
    public bool TelegramNotificationsEnabled { get; set; }

    /// <summary>Pushover user (or group) key the operator's Pushover app delivers to.</summary>
    public string? PushoverUserKey { get; set; }
    public bool PushoverNotificationsEnabled { get; set; }

    /// <summary>
    /// Optional ISO 4217 code to show converted prices in, beside the native price. Display only —
    /// null means off. Also the signal that makes the worker fetch exchange rates at all.
    /// </summary>
    public string? DisplayCurrency { get; set; }

    /// <summary>
    /// Whether price-alert email is delivered to this account. Defaults to on — email is the
    /// channel the product promises, so an account that never touches this keeps receiving it.
    /// Governs alert email only: password-reset mail and the SMTP test send are unconditional.
    /// </summary>
    public bool EmailNotificationsEnabled { get; set; } = true;
    public int? AnomalyThresholdPercent { get; set; }
    public int? AutoPauseAfterFailures { get; set; }

    /// <summary>
    /// Replaces the password hash and rotates the security stamp, invalidating every
    /// session issued before the change. Also clears any pending reset token so a
    /// previously emailed link can't undo the new password.
    /// </summary>
    public void ChangePassword(string newPasswordHash)
    {
        PasswordHash = newPasswordHash;
        SecurityStamp = NewSecurityStamp();
        PasswordResetTokenHash = null;
        PasswordResetTokenExpiresAt = null;
    }

    /// <summary>
    /// Replaces the hash of the <em>same</em> password with one made under the current hasher
    /// parameters. Unlike <see cref="ChangePassword"/> it keeps the security stamp, so existing
    /// sessions stay valid.
    /// </summary>
    public void UpgradePasswordHash(string rehashedPassword) => PasswordHash = rehashedPassword;

    /// <summary>
    /// Updates display name and login email (normalized to lowercase). Does not rotate
    /// the security stamp — profile edits keep existing sessions alive.
    /// </summary>
    public void UpdateProfile(string name, string email)
    {
        Name = name;
        Email = email.ToLowerInvariant();
    }

    private static string NewSecurityStamp() => Guid.NewGuid().ToString("N");

    // Navigation properties
    public ICollection<Product> Products { get; init; } = [];
    public ICollection<Tag> Tags { get; init; } = [];
    public ICollection<ComparisonGroup> ComparisonGroups { get; init; } = [];
    public ICollection<Alert> Alerts { get; init; } = [];
    public ICollection<Notification> Notifications { get; init; } = [];
    public ICollection<WebhookTarget> WebhookTargets { get; init; } = [];
    public ICollection<ApiKey> ApiKeys { get; init; } = [];
}
