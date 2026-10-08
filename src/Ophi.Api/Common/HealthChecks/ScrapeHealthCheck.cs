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
            var recent = dbContext.ScrapeLogs.Where(l => l.CreatedAt >= cutoff);
            var total = await recent.CountAsync(cancellationToken);

            if (total == 0)
            {
                return HealthCheckResult.Healthy("No scrapes in the last hour",
                    new Dictionary<string, object> { ["totalScrapes"] = 0 });
            }

            var successCount = await recent.CountAsync(l => l.Success, cancellationToken);
            var successRate = (double)successCount / total;
            var data = new Dictionary<string, object>
            {
                ["successRate"] = Math.Round(successRate, 3),
                ["totalScrapes"] = total,
                ["successCount"] = successCount,
                ["failureCount"] = total - successCount
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
