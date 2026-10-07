using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Infrastructure.Persistence;
using Ophi.Worker.Settings;

namespace Ophi.Worker.Services;

public class DataRetentionService(
    IServiceProvider serviceProvider,
    IOptions<WorkerSettings> workerSettings,
    TimeProvider timeProvider,
    ILogger<DataRetentionService> logger)
{
    private DateTime _lastCleanupUtc = DateTime.MinValue;
    private static readonly TimeSpan CleanupCooldown = TimeSpan.FromHours(24);
    private int _running;

    public async Task CleanupIfDueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (now - _lastCleanupUtc < CleanupCooldown)
            return;

        // Prevent concurrent cleanup runs
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return;

        var settings = workerSettings.Value;
        var cutoff = now.AddDays(-settings.ScrapeLogRetentionDays);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OphiDbContext>();

            var scrapeLogDeleted = await dbContext.ScrapeLogs
                .Where(s => s.CreatedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            if (scrapeLogDeleted > 0)
            {
                logger.LogInformation("Data retention: deleted {Count} scrape log entries older than {Days} days",
                    scrapeLogDeleted, settings.ScrapeLogRetentionDays);
            }

            var notificationCutoff = now.AddDays(-settings.ReadNotificationRetentionDays);
            var notificationsDeleted = await dbContext.Notifications
                .Where(n => n.IsRead && n.CreatedAt < notificationCutoff)
                .ExecuteDeleteAsync(cancellationToken);

            if (notificationsDeleted > 0)
            {
                logger.LogInformation("Data retention: deleted {Count} read notifications older than {Days} days",
                    notificationsDeleted, settings.ReadNotificationRetentionDays);
            }

            // Stamp timestamp only after successful cleanup so a failure retries on the next cycle
            _lastCleanupUtc = timeProvider.GetUtcNow().UtcDateTime;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during data retention cleanup");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }
}
