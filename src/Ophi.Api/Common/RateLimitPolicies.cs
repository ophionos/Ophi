using System.Security.Claims;
using System.Threading.RateLimiting;

namespace Ophi.Api.Common;

public static class RateLimitPolicies
{
    public const string ProductCreation = "product-creation";
    public const string AlertCreation = "alert-creation";
    public const string StoreCreation = "store-creation";
    public const string WebhookCreation = "webhook-creation";

    /// <summary>
    /// Endpoints that make the server fetch or post to a caller-chosen URL (store detect/test, webhook
    /// and notification-channel tests). Each call can hold an outbound request for the full HTTP timeout.
    /// </summary>
    public const string OutboundFetch = "outbound-fetch";

    private static readonly Dictionary<string, SlidingWindowRateLimiterOptions> Limits = new()
    {
        [ProductCreation] = new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 50,
            Window = TimeSpan.FromHours(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0
        },
        [AlertCreation] = new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromHours(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0
        },
        [StoreCreation] = new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromHours(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0
        },
        [WebhookCreation] = new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0
        },
        [OutboundFetch] = new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0
        }
    };

    public static SlidingWindowRateLimiterOptions? GetLimiterOptions(string policyName) =>
        Limits.GetValueOrDefault(policyName);

    public static string GetPartitionKey(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId != null) return userId;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
    }

    public static async Task HandleRejection(HttpContext httpContext, CancellationToken token = default)
    {
        var userId = GetPartitionKey(httpContext);
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var path = httpContext.Request.Path.Value;

        var loggerFactory = httpContext.RequestServices.GetService(typeof(ILoggerFactory))
            as ILoggerFactory;
        var logger = loggerFactory?.CreateLogger("Ophi.Api.RateLimiting");
        logger?.LogWarning(
            "Rate limit exceeded: user={UserId} ip={IP} path={Path}",
            userId, ip, path);

        httpContext.Response.StatusCode = 429;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsync(
            """{"error":"TooManyRequests","message":"Too many requests. Please try again later."}""", token);
    }
}
