using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Ophi.Api.Common.Middleware;

namespace Ophi.Api.Common.Startup;

internal static class RateLimitSetup
{
    /// <summary>Global limiter cap per partition (a user, or the client IP when anonymous), per minute.</summary>
    internal const int GlobalPerPartitionLimit = 120;

    /// <summary>
    /// Pre-authentication cap per client IP, per minute. Above the global cap because every user
    /// behind one proxy address shares it (the compose stack without trusted forwarded headers).
    /// </summary>
    internal const int PreAuthPerIpLimit = 600;

    /// <summary>
    /// Registers the global per-user/IP limiter plus the per-policy limiters used by
    /// auth, creation and outbound-fetch endpoints, and the pre-authentication per-IP guard.
    /// Development gets a relaxed global cap so E2E suites don't hit it.
    /// </summary>
    public static IServiceCollection AddOphiRateLimiting(this IServiceCollection services, IWebHostEnvironment env)
    {
        // Rate limiting protects production; in Development it is effectively
        // disabled so the E2E suite can register/log in many throwaway users from
        // a single (localhost) IP — the dev-server proxy and tests all share one
        // partition, and a per-IP fixed window otherwise accumulates across the
        // suite and back-to-back re-runs. The Testing env keeps the real caps.
        var globalRateLimit = env.IsDevelopment() ? Unlimited : GlobalPerPartitionLimit;
        var authRateLimit = env.IsDevelopment() ? Unlimited : 10;

        services.AddSingleton(new PreAuthRateLimiter(env.IsDevelopment() ? Unlimited : PreAuthPerIpLimit));

        services.AddRateLimiter(options =>
        {
            // Per user once authenticated — which is why UseRateLimiter runs after UseAuthentication.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: RateLimitPolicies.GetPartitionKey(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = globalRateLimit,
                        Window = TimeSpan.FromSeconds(60),
                        QueueLimit = 0
                    }));

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
            AddCreationPolicy(options, RateLimitPolicies.OutboundFetch, env);

            options.OnRejected = async (context, token) =>
            {
                await RateLimitPolicies.HandleRejection(context.HttpContext, token);
            };
        });

        return services;
    }

    private const int Unlimited = 1_000_000;

    /// <summary>
    /// Authentication and authorization with the rate limiters around them, in the one order that works:
    /// the per-IP guard first (it bounds the API-key lookup), then authentication, then the main limiter
    /// (so its partitions see the user), then authorization (so 401/403 responses still count).
    /// </summary>
    public static IApplicationBuilder UseOphiAuthenticationAndRateLimiting(this IApplicationBuilder app, bool enableRateLimiting)
    {
        if (enableRateLimiting)
            app.UseMiddleware<PreAuthRateLimitMiddleware>();

        app.UseAuthentication();

        if (enableRateLimiting)
            app.UseRateLimiter();

        app.UseAuthorization();
        return app;
    }

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
