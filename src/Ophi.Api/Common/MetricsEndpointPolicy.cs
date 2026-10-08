using System.Security.Cryptography;
using System.Text;

namespace Ophi.Api.Common;

internal static class MetricsEndpointPolicy
{
    /// <summary>
    /// /metrics is mapped when a token protects it, or outside Production (open, for local use).
    /// Production without a token leaves it unmapped so the instance runs without one.
    /// </summary>
    public static bool IsEnabled(IWebHostEnvironment environment, string? metricsToken) =>
        !environment.IsProduction() || !string.IsNullOrWhiteSpace(metricsToken);

    /// <summary>
    /// True when <paramref name="authorizationHeader"/> is exactly <c>Bearer {metricsToken}</c>.
    /// Constant-time over the bytes so the comparison does not leak how much of the token matched.
    /// </summary>
    public static bool IsAuthorized(string authorizationHeader, string metricsToken) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(authorizationHeader),
            Encoding.UTF8.GetBytes($"Bearer {metricsToken}"));
}
