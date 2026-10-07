using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wolverine;

namespace Ophi.Api.Common.HealthChecks;

public class WolverineHealthCheck(IServiceProvider serviceProvider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var bus = serviceProvider.GetService<IMessageBus>();
            return Task.FromResult(bus is not null
                ? HealthCheckResult.Healthy("Wolverine message bus is available")
                : HealthCheckResult.Unhealthy("Wolverine message bus is not registered"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Wolverine message bus check failed", ex));
        }
    }
}
