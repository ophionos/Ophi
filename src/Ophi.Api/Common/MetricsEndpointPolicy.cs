namespace Ophi.Api.Common;

internal static class MetricsEndpointPolicy
{
    /// <summary>
    /// /metrics is mapped when a token protects it, or outside Production (open, for local use).
    /// Production without a token leaves it unmapped so the instance runs without one.
    /// </summary>
    public static bool IsEnabled(IWebHostEnvironment environment, string? metricsToken) =>
        !environment.IsProduction() || !string.IsNullOrWhiteSpace(metricsToken);
}
