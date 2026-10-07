using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Ophi.Api.Common.Startup;

internal static class RateLimitSetup
{
    /// <summary>
    /// Registers the global per-user/IP limiter plus the per-policy limiters used by
    /// auth and creation endpoints. Development gets a relaxed global cap so E2E
    /// suites don't hit it.
    /// </summary>
    public static IServiceCollection AddOphiRateLimiting(this IServiceCollection services, IWebHostEnvironment env)
    {
        // Rate limiting protects production; in Development it is effectively
        // disabled so the E2E suite can register/log in many throwaway users from
        // a single (localhost) IP — the dev-server proxy and tests all share one
        // partition, and a per-IP fixed window otherwise accumulates across the
        // suite and back-to-back re-runs. The Testing env keeps the real caps.
        var globalRateLimit = env.IsDevelopment() ? Unlimited : 120;
        var authRateLimit = env.IsDevelopment() ? Unlimited : 10;

        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var key = context.User.Identity?.IsAuthenticated == true
                    ? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? context.Connection.RemoteIpAddress?.ToString()
                    : context.Connection.RemoteIpAddress?.ToString();

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: key ?? "anonymous",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = globalRateLimit,
                        Window = TimeSpan.FromSeconds(60),
                        QueueLimit = 0
                    });
            });

            options.AddPolicy("auth", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = authRateLimit,
                        Window = TimeSpan.FromSeconds(60),
                        QueueLimit = 0
                    }));

            // Creation policies get the same Development relaxation as the global/auth
            // limiters above: the e2e suite seeds dozens of products per run through
            // the same partition, and the production sliding windows 429 it mid-suite.
            // Testing keeps the real caps (the rate-limit tests assert them there).
            AddCreationPolicy(options, RateLimitPolicies.ProductCreation, env);
            AddCreationPolicy(options, RateLimitPolicies.AlertCreation, env);
            AddCreationPolicy(options, RateLimitPolicies.StoreCreation, env);
            AddCreationPolicy(options, RateLimitPolicies.WebhookCreation, env);

            options.OnRejected = async (context, token) =>
            {
                await RateLimitPolicies.HandleRejection(context.HttpContext, token);
            };
        });

        return services;
    }

    private const int Unlimited = 1_000_000;

    private static void AddCreationPolicy(RateLimiterOptions options, string policyName, IWebHostEnvironment env)
    {
        var limiterOptions = RateLimitPolicies.GetLimiterOptions(policyName)!;
        var permitLimit = env.IsDevelopment() ? Unlimited : limiterOptions.PermitLimit;
        options.AddPolicy(policyName, context =>
            RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: RateLimitPolicies.GetPartitionKey(context),
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = limiterOptions.Window,
                    SegmentsPerWindow = limiterOptions.SegmentsPerWindow,
                    QueueLimit = limiterOptions.QueueLimit
                }));
    }
}
