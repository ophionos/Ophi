using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ophi.Infrastructure.Net;

/// <summary>
/// The Playwright side of the SSRF guard: a SOCKS5 proxy on 127.0.0.1 that Chromium is launched
/// through. Chromium sends the hostname (remote DNS), so this proxy — not the browser — resolves it and
/// connects with <see cref="PinnedConnector"/>, the same check-then-connect as the HTTP path. Every
/// connection the browser opens (navigation, each redirect hop, sub-resources, WebSockets) passes here,
/// and the checked address is the connected address (no DNS-rebinding window). TLS runs end-to-end
/// through the tunnel, so the browser's fingerprint does not change. WebRTC UDP ignores SOCKS proxies;
/// <see cref="Scraping.PlaywrightBrowserManager"/> turns it off with a launch flag.
/// </summary>
public sealed class PinnedSocksProxy : IAsyncDisposable
{
    /// <summary>SOCKS5 REP "connection not allowed by ruleset": every address of the host is blocked.</summary>
    public const byte ReplyNotAllowed = 0x02;

    private const byte ReplySucceeded = 0x00;
    private const byte ReplyGeneralFailure = 0x01;
    private const byte ReplyNetworkUnreachable = 0x03;
    private const byte ReplyHostUnreachable = 0x04;
    private const byte ReplyConnectionRefused = 0x05;
    private const byte ReplyTtlExpired = 0x06;
    private const byte ReplyCommandNotSupported = 0x07;
    private const byte ReplyAddressTypeNotSupported = 0x08;

    /// <summary>Greeting, request and the outbound connect must finish within this, or the client is dropped.</summary>
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(30);

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly Func<IPAddress, bool> _isBlocked;
    private Task? _acceptLoop;

    private PinnedSocksProxy(
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPAddress, bool> isBlocked)
    {
        _resolve = resolve;
        _isBlocked = isBlocked;
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>The value for Playwright's <c>Proxy.Server</c>.</summary>
    public string Server => $"socks5://127.0.0.1:{Port}";

    /// <summary>Starts listening on an ephemeral loopback port.</summary>
    public static PinnedSocksProxy Start(
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null,
        Func<IPAddress, bool>? isBlocked = null)
    {
        var proxy = new PinnedSocksProxy(resolve ?? Dns.GetHostAddressesAsync, isBlocked ?? AddressPolicy.IsBlocked);
        proxy._listener.Start();
        proxy._acceptLoop = proxy.AcceptLoopAsync();
        return proxy;
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (true)
            {
                var client = await _listener.AcceptSocketAsync(_stop.Token);
                _ = HandleAsync(client);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task HandleAsync(Socket clientSocket)
    {
        clientSocket.NoDelay = true;
        await using var client = new NetworkStream(clientSocket, ownsSocket: true);
        Socket? upstreamSocket = null;
        try
        {
            using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
            {
                handshake.CancelAfter(HandshakeTimeout);
                upstreamSocket = await HandshakeAsync(client, handshake.Token);
            }

            if (upstreamSocket is null)
                return;

            await using var upstream = new NetworkStream(upstreamSocket, ownsSocket: true);
            upstreamSocket = null;
            await RelayAsync(client, upstream, _stop.Token);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException
                                       or ObjectDisposedException or InvalidDataException)
        {
            // The client went away, sent garbage, or timed out. Nothing to report back to.
        }
        finally
        {
            upstreamSocket?.Dispose();
        }
    }

    /// <returns>The connected upstream socket, or null when the request was refused (reply already sent).</returns>
    private async Task<Socket?> HandshakeAsync(NetworkStream client, CancellationToken ct)
    {
        // Greeting: VER NMETHODS METHODS...
        var head = new byte[2];
        await client.ReadExactlyAsync(head, ct);
        if (head[0] != 0x05)
            throw new InvalidDataException("Not SOCKS5.");
        var methods = new byte[head[1]];
        await client.ReadExactlyAsync(methods, ct);
        if (!methods.Contains((byte)0x00))
        {
            await client.WriteAsync(new byte[] { 0x05, 0xFF }, ct);
            return null;
        }
        await client.WriteAsync(new byte[] { 0x05, 0x00 }, ct);

        // Request: VER CMD RSV ATYP DST.ADDR DST.PORT
        var request = new byte[4];
        await client.ReadExactlyAsync(request, ct);
        if (request[0] != 0x05)
            throw new InvalidDataException("Not SOCKS5.");

        string host;
        switch (request[3])
        {
            case 0x01:
                host = new IPAddress(await ReadAsync(client, 4, ct)).ToString();
                break;
            case 0x03:
                var length = (await ReadAsync(client, 1, ct))[0];
                host = Encoding.ASCII.GetString(await ReadAsync(client, length, ct));
                break;
            case 0x04:
                host = new IPAddress(await ReadAsync(client, 16, ct)).ToString();
                break;
            default:
                await ReplyAsync(client, ReplyAddressTypeNotSupported, ct);
                return null;
        }
        var port = BinaryPrimitives.ReadUInt16BigEndian(await ReadAsync(client, 2, ct));

        if (request[1] != 0x01)
        {
            // Only CONNECT. BIND and UDP ASSOCIATE would open other paths around the check.
            await ReplyAsync(client, ReplyCommandNotSupported, ct);
            return null;
        }

        Socket upstream;
        try
        {
            upstream = await PinnedConnector.ConnectAsync(host, port, _resolve, _isBlocked, ct);
        }
        catch (BlockedDestinationException)
        {
            await ReplyAsync(client, ReplyNotAllowed, ct);
            return null;
        }
        catch (SocketException ex)
        {
            await ReplyAsync(client, ex.SocketErrorCode switch
            {
                SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain
                    or SocketError.HostUnreachable => ReplyHostUnreachable,
                SocketError.NetworkUnreachable or SocketError.NetworkDown => ReplyNetworkUnreachable,
                SocketError.ConnectionRefused => ReplyConnectionRefused,
                SocketError.TimedOut => ReplyTtlExpired,
                _ => ReplyGeneralFailure
            }, ct);
            return null;
        }

        try
        {
            await ReplyAsync(client, ReplySucceeded, ct);
            return upstream;
        }
        catch
        {
            upstream.Dispose();
            throw;
        }
    }

    private static async Task<byte[]> ReadAsync(NetworkStream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, ct);
        return buffer;
    }

    /// <summary>VER REP RSV ATYP=IPv4 BND.ADDR=0.0.0.0 BND.PORT=0 — clients ignore the bound address for CONNECT.</summary>
    private static async Task ReplyAsync(NetworkStream client, byte code, CancellationToken ct) =>
        await client.WriteAsync(new byte[] { 0x05, code, 0x00, 0x01, 0, 0, 0, 0, 0, 0 }, ct);

    private static async Task RelayAsync(NetworkStream client, NetworkStream upstream, CancellationToken ct)
    {
        var up = PumpAsync(client, upstream, ct);
        var down = PumpAsync(upstream, client, ct);
        // When one side finishes, the half-close is forwarded; wait for the other so a response that
        // follows a client half-close still arrives.
        await Task.WhenAll(up, down);
    }

    private static async Task PumpAsync(NetworkStream from, NetworkStream to, CancellationToken ct)
    {
        try
        {
            await from.CopyToAsync(to, ct);
            to.Socket.Shutdown(SocketShutdown.Send);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // One side reset: tear both down so the other pump ends too.
            from.Socket.Close();
            to.Socket.Close();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        if (_acceptLoop is not null)
            await _acceptLoop;
        _stop.Dispose();
    }
}
