using Ophi.Api.Common.HealthChecks;
using Prometheus;

namespace Ophi.Api.Common.Startup;

internal static class HealthAndMetricsRouting
{
    /// <summary>
    /// Maps the three health endpoints (liveness, readiness, legacy /health) and the
    /// Prometheus /metrics scrape endpoint with token enforcement (unmapped in Production without a token).
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
        if (!MetricsEndpointPolicy.IsEnabled(app.Environment, metricsToken))
        {
            // Unmapped rather than open: the gauges expose every user's tracked products and prices.
            app.Logger.LogInformation("MetricsToken is not configured; /metrics is disabled.");
            return app;
        }

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
                    if (!MetricsEndpointPolicy.IsAuthorized(authHeader, metricsToken))
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
