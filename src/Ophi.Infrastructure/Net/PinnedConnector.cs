using System.Net;
using System.Net.Sockets;

namespace Ophi.Infrastructure.Net;

/// <summary>
/// Resolve, check, connect — the one place both SSRF guards open outbound connections
/// (<see cref="PublicAddressHandler"/> for HttpClient, <see cref="PinnedSocksProxy"/> for Chromium).
/// The address that is checked is the address that is connected to, so a DNS answer that changes after
/// the check (rebinding) cannot redirect the connection. A host that <see cref="UpstreamProxy.Routes"/>
/// is reached through the upstream proxy, which is asked to tunnel to the checked address.
/// </summary>
internal static class PinnedConnector
{
    /// <exception cref="BlockedDestinationException">Every resolved address is blocked.</exception>
    /// <exception cref="SocketException">The name does not resolve, or no allowed address accepts.</exception>
    public static async Task<Socket> ConnectAsync(
        string host,
        int port,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPAddress, bool> isBlocked,
        CancellationToken cancellationToken,
        UpstreamProxy? upstream = null)
    {
        host = host.Trim('[', ']');
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await resolve(host, cancellationToken);
        var viaUpstream = upstream?.Routes(host) == true;

        if (addresses.Length == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        var allowed = addresses.Where(a => !isBlocked(a)).ToArray();
        if (allowed.Length == 0)
            throw new BlockedDestinationException();

        IOException? lastUpstreamError = null;
        SocketException? lastError = null;
        foreach (var address in allowed)
        {
            if (viaUpstream)
            {
                try
                {
                    return await upstream!.ConnectAsync(address, port, cancellationToken);
                }
                catch (UpstreamProxyException ex)
                {
                    lastUpstreamError = ex;
                }
                catch (SocketException ex)
                {
                    lastError = ex;
                }
                continue;
            }

            // Per-family socket: a dual-mode socket fails where IPv6 is disabled (some containers).
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                return socket;
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                lastError = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        if (lastUpstreamError is not null)
            throw lastUpstreamError;
        throw lastError!;
    }

    /// <summary>
    /// The pre-check for a start URL: true when <paramref name="host"/> resolves and every address is
    /// blocked — the case <see cref="ConnectAsync"/> refuses with <see cref="BlockedDestinationException"/>.
    /// A name that does not resolve is not "blocked"; the caller's own fetch reports it as a network error.
    /// </summary>
    public static async Task<bool> IsRefusedAsync(
        string host,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPAddress, bool> isBlocked,
        CancellationToken cancellationToken)
    {
        host = host.Trim('[', ']');
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await resolve(host, cancellationToken);
        }
        catch (SocketException)
        {
            return false;
        }

        return addresses.Length > 0 && addresses.All(isBlocked);
    }
}
