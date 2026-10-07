using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Common.HealthChecks;

public class ScrapeHealthCheck(OphiDbContext dbContext, TimeProvider timeProvider) : IHealthCheck
{
    internal const double MinSuccessRate = 0.80;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddHours(-1);
            var logs = await dbContext.ScrapeLogs
                .Where(l => l.CreatedAt >= cutoff)
                .Select(l => l.Success)
                .ToListAsync(cancellationToken);

            if (logs.Count == 0)
            {
                return HealthCheckResult.Healthy("No scrapes in the last hour",
                    new Dictionary<string, object> { ["totalScrapes"] = 0 });
            }

            var successCount = logs.Count(s => s);
            var successRate = (double)successCount / logs.Count;
            var data = new Dictionary<string, object>
            {
                ["successRate"] = Math.Round(successRate, 3),
                ["totalScrapes"] = logs.Count,
                ["successCount"] = successCount,
                ["failureCount"] = logs.Count - successCount
            };

            return successRate >= MinSuccessRate
                ? HealthCheckResult.Healthy($"Scrape success rate: {successRate:P0}", data)
                : HealthCheckResult.Degraded($"Scrape success rate below threshold: {successRate:P0} < {MinSuccessRate:P0}", data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Scrape health check failed", ex);
        }
    }
}
