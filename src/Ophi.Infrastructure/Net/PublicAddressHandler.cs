using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;

namespace Ophi.Infrastructure.Net;

/// <summary>
/// The primary handler for every HTTP client that fetches a user-chosen URL. It resolves the host
/// itself, drops every address <see cref="AddressPolicy"/> blocks, and connects to an address it checked
/// — so the check and the connection use the same address (no DNS-rebinding window). Redirects are
/// followed by the handler, and each new connection goes through the same callback, so every hop is
/// checked. TLS still uses the request host for SNI and certificate validation.
/// </summary>
public static class PublicAddressHandler
{
    /// <summary>Fixed text: it must never carry the resolved address, which would reveal internal DNS.</summary>
    public const string BlockedMessage = "The destination resolves to a private or reserved network address.";

    public static HttpMessageHandler Create(
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null,
        Func<IPAddress, bool>? isBlocked = null,
        UpstreamProxy? upstream = null)
    {
        resolve ??= Dns.GetHostAddressesAsync;
        isBlocked ??= AddressPolicy.IsBlocked;

        return new SocketsHttpHandler
        {
            // With a system proxy the callback would connect to (and check) the proxy, not the target.
            // An UpstreamProxy is different: the callback checks the target, then tunnels to that address.
            UseProxy = false,
            ConnectCallback = async (context, cancellationToken) =>
                new NetworkStream(
                    await PinnedConnector.ConnectAsync(
                        context.DnsEndPoint.Host, context.DnsEndPoint.Port, resolve, isBlocked, cancellationToken,
                        upstream),
                    ownsSocket: true)
        };
    }

    /// <summary>True when <paramref name="exception"/> (or an inner one) is a blocked-destination refusal.</summary>
    public static bool IsBlockedDestination(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is BlockedDestinationException)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Uses <see cref="Create"/> as this client's primary handler. Apply it to every client that fetches
    /// or posts to a URL a user chose.
    /// </summary>
    public static IHttpClientBuilder UsePublicAddressesOnly(this IHttpClientBuilder builder) =>
        builder.ConfigurePrimaryHttpMessageHandler(() => Create());

    /// <summary>
    /// As <see cref="UsePublicAddressesOnly(IHttpClientBuilder)"/>, plus the operator's
    /// <see cref="UpstreamProxy"/> for its listed domains. For the scraper only.
    /// </summary>
    public static IHttpClientBuilder UsePublicAddressesWithUpstreamProxy(this IHttpClientBuilder builder) =>
        builder.ConfigurePrimaryHttpMessageHandler(sp =>
            Create(upstream: sp.GetRequiredService<UpstreamProxy>()));

    /// <summary>
    /// As <see cref="UsePublicAddressesOnly(IHttpClientBuilder)"/>, with the webhook policy: the
    /// operator's <see cref="WebhookAddressPolicy.AllowedNetworks"/> are also reachable. The check still
    /// runs on the resolved, pinned address of every hop.
    /// </summary>
    public static IHttpClientBuilder UseWebhookAddressPolicy(this IHttpClientBuilder builder) =>
        builder.ConfigurePrimaryHttpMessageHandler(sp =>
            Create(isBlocked: sp.GetRequiredService<WebhookAddressPolicy>().IsBlocked));
}

public sealed class BlockedDestinationException() : IOException(PublicAddressHandler.BlockedMessage);
