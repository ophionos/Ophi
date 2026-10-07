namespace Ophi.Api.Common;

internal static class MetricsAuthGuard
{
    public static void EnsureMetricsTokenConfigured(IWebHostEnvironment environment, string? metricsToken)
    {
        if (environment.IsProduction() && string.IsNullOrWhiteSpace(metricsToken))
        {
            throw new InvalidOperationException(
                "MetricsToken must be configured in Production. Set the MetricsToken configuration value " +
                "(e.g., via environment variable) to a non-empty secret to protect the /metrics endpoint.");
        }
    }
}
