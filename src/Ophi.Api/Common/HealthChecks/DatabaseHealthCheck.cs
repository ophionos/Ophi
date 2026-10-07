using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Common.HealthChecks;

public class DatabaseHealthCheck(OphiDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database is reachable")
                : HealthCheckResult.Unhealthy("Database connection failed");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database check threw an exception", ex);
        }
    }
}
