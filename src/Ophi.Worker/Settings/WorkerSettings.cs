namespace Ophi.Worker.Settings;

public class WorkerSettings
{
    public const string SectionName = "Worker";

    /// <summary>
    /// Interval in seconds between dispatch cycles.
    /// </summary>
    public int DispatchIntervalSeconds { get; init; } = 60;

    /// <summary>
    /// Maximum number of products to dispatch per cycle.
    /// </summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>
    /// Number of consecutive failures before a product is marked as Error.
    /// </summary>
    public int MaxFailuresBeforeError { get; init; } = 3;

    /// <summary>
    /// Number of days to retain scrape log entries before cleanup.
    /// </summary>
    public int ScrapeLogRetentionDays { get; init; } = 30;

    /// <summary>
    /// Number of days to retain read notifications before cleanup.
    /// </summary>
    public int ReadNotificationRetentionDays { get; init; } = 90;

    /// <summary>
    /// Number of consecutive suspicious scrapes before auto-pausing a URL.
    /// </summary>
    public int MaxSuspiciousBeforePause { get; init; } = 3;

    /// <summary>
    /// Threshold for price change to be considered suspicious (0.7 = 70%).
    /// </summary>
    public decimal PriceAnomalyThreshold { get; init; } = 0.7m;
}
