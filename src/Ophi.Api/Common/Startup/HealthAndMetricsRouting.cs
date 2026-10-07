using Ophi.Api.Common.HealthChecks;
using Prometheus;

namespace Ophi.Api.Common.Startup;

internal static class HealthAndMetricsRouting
{
    /// <summary>
    /// Maps the three health endpoints (liveness, readiness, legacy /health) and the
    /// Prometheus /metrics scrape endpoint with token enforcement.
    /// </summary>
    public static WebApplication MapHealthAndMetrics(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new()
        {
            Predicate = _ => false, // No checks — if the process responds, it's alive
            ResponseWriter = HealthCheckResponseWriter.WriteResponse
        });

        app.MapHealthChecks("/health/ready", new()
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = HealthCheckResponseWriter.WriteResponse
        });

        // Legacy /health — maps to readiness for backwards compatibility.
        app.MapHealthChecks("/health", new()
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = HealthCheckResponseWriter.WriteResponse
        });

        var metricsToken = app.Configuration["MetricsToken"];
        MetricsAuthGuard.EnsureMetricsTokenConfigured(app.Environment, metricsToken);
        if (string.IsNullOrWhiteSpace(metricsToken))
        {
            app.Logger.LogWarning(
                "MetricsToken is not configured; /metrics is unauthenticated. " +
                "Set MetricsToken to require a bearer token. (Allowed outside Production.)");
        }
        app.MapMetrics("/metrics").Add(endpointBuilder =>
        {
            if (!string.IsNullOrWhiteSpace(metricsToken))
            {
                var original = endpointBuilder.RequestDelegate!;
                endpointBuilder.RequestDelegate = async context =>
                {
                    var authHeader = context.Request.Headers.Authorization.ToString();
                    if (!authHeader.Equals($"Bearer {metricsToken}", StringComparison.Ordinal))
                    {
                        context.Response.StatusCode = 401;
                        return;
                    }

                    await original(context);
                };
            }
        });

        return app;
    }
}
