using System.Threading.RateLimiting;

namespace Ophi.Api.Common.Middleware;

/// <summary>
/// A per-IP limiter that runs before authentication. The main limiter runs after authentication so its
/// partitions are per user; without this guard, a flood of made-up API keys would reach the API-key
/// database lookup unthrottled.
/// </summary>
public sealed class PreAuthRateLimiter(int permitLimit) : IDisposable
{
    internal PartitionedRateLimiter<HttpContext> Limiter { get; } =
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(60),
                    QueueLimit = 0
                }));

    public void Dispose() => Limiter.Dispose();
}

public class PreAuthRateLimitMiddleware(RequestDelegate next, PreAuthRateLimiter limiter)
{
    public async Task InvokeAsync(HttpContext context)
    {
        using var lease = limiter.Limiter.AttemptAcquire(context);
        if (!lease.IsAcquired)
        {
            await RateLimitPolicies.HandleRejection(context, context.RequestAborted);
            return;
        }

        await next(context);
    }
}
