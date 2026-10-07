using Microsoft.AspNetCore.HttpOverrides;

namespace Ophi.Api.Common.Startup;

internal static class ForwardedHeadersSetup
{
    public const string KnownIPNetworksKey = "ForwardedHeaders:KnownIPNetworks";

    /// <summary>
    /// Honours X-Forwarded-For from trusted proxies so RemoteIpAddress (and every per-IP rate-limit
    /// partition keyed on it) is the real client, not the proxy. Loopback is trusted by default;
    /// extra proxy networks come from <c>ForwardedHeaders:KnownIPNetworks</c> as a comma-separated
    /// CIDR list. Only the last hop is read (ForwardLimit = 1): each deployment has exactly one
    /// proxy in front of the API, and anything further left in the header is client-controlled.
    /// </summary>
    public static IServiceCollection AddOphiForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        // Parse eagerly so a typo in the CIDR list fails at startup, not on the first request.
        var networks = (configuration[KnownIPNetworksKey] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(System.Net.IPNetwork.Parse)
            .ToList();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = 1;
            foreach (var network in networks)
                options.KnownIPNetworks.Add(network);
        });

        return services;
    }
}
