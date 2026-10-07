namespace Ophi.Infrastructure.Settings;

public class AlertSettings
{
    public const string SectionName = "Alerts";

    /// <summary>
    /// Maximum number of active alerts a user can have.
    /// </summary>
    public int MaxAlertsPerUser { get; init; } = 100;

    /// <summary>
    /// Cooldown period in minutes before an alert can be triggered again.
    /// </summary>
    public int CooldownMinutes { get; init; } = 60;
}
