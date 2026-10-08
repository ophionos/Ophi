using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ophi.Infrastructure.Net;

/// <summary>
/// An optional upstream proxy (HTTP CONNECT or SOCKS5) for scrapes of operator-listed domains, for stores
/// that refuse the deployment's own egress. <see cref="PinnedConnector"/> still resolves and checks the
/// target; the proxy is asked to tunnel to that checked IP address and never sees the hostname, so it
/// cannot resolve the name to an internal address. The proxy itself is trusted operator infrastructure:
/// its own address is not checked. The route depends only on the target host, so a pooled connection
/// to a host always took the same route.
/// </summary>
public sealed class UpstreamProxy
{
    public const string UrlKey = "Scraping:UpstreamProxy:Url";
    public const string DomainsKey = "Scraping:UpstreamProxy:Domains";

    private const int MaxResponseHeadBytes = 8192;

    public static readonly UpstreamProxy None = new(null, null, 0, null, []);

    private readonly string? _scheme;
    private readonly string? _host;
    private readonly int _port;
    private readonly (string User, string Password)? _credentials;

    private UpstreamProxy(string? scheme, string? host, int port, (string, string)? credentials, IReadOnlyList<string> domains)
    {
        _scheme = scheme;
        _host = host;
        _port = port;
        _credentials = credentials;
        Domains = domains;
    }

    public bool IsEnabled => _scheme is not null;

    /// <summary>Lower-case domains; each also covers its subdomains.</summary>
    public IReadOnlyList<string> Domains { get; }

    /// <summary>
    /// Parses <see cref="UrlKey"/> (<c>http://</c> or <c>socks5://</c>, optional <c>user:password@</c>)
    /// and <see cref="DomainsKey"/> (comma-separated). Both empty is <see cref="None"/>. Anything else
    /// invalid stops startup. The messages never contain the URL, which may hold a password.
    /// </summary>
    public static UpstreamProxy FromConfiguration(string? url, string? domains)
    {
        var hasUrl = !string.IsNullOrWhiteSpace(url);
        var domainList = (domains ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.TrimStart('*').TrimStart('.').TrimEnd('.').ToLowerInvariant())
            .ToList();

        if (!hasUrl && domainList.Count == 0)
            return None;
        if (!hasUrl)
            throw new InvalidOperationException($"{DomainsKey} is set, but {UrlKey} is empty.");
        if (domainList.Count == 0)
            throw new InvalidOperationException($"{UrlKey} is set, but {DomainsKey} lists no domains.");

        foreach (var domain in domainList)
        {
            if (Uri.CheckHostName(domain) != UriHostNameType.Dns)
                throw new InvalidOperationException(
                    $"{DomainsKey} entry '{domain}' is not a domain name (no scheme, port or path).");
        }

        if (!Uri.TryCreate(url!.Trim(), UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"{UrlKey} is not an absolute URL.");
        if (uri.Scheme is not ("http" or "socks5"))
            throw new InvalidOperationException($"{UrlKey} must use http:// or socks5://, not {uri.Scheme}://.");
        if (uri.Port <= 0)
            throw new InvalidOperationException($"{UrlKey} must name a port.");

        (string, string)? credentials = null;
        if (uri.UserInfo.Length > 0)
        {
            var separator = uri.UserInfo.IndexOf(':');
            credentials = separator < 0
                ? (Uri.UnescapeDataString(uri.UserInfo), "")
                : (Uri.UnescapeDataString(uri.UserInfo[..separator]), Uri.UnescapeDataString(uri.UserInfo[(separator + 1)..]));
        }

        return new UpstreamProxy(uri.Scheme, uri.Host.Trim('[', ']'), uri.Port, credentials, domainList);
    }

    /// <summary>True when connections to <paramref name="host"/> go through the proxy.</summary>
    public bool Routes(string host)
    {
        if (!IsEnabled)
            return false;

        host = host.TrimEnd('.').ToLowerInvariant();
        return Domains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
    }

    /// <summary>Opens a tunnel through the proxy to <paramref name="target"/>, an address the caller checked.</summary>
    /// <exception cref="UpstreamProxyException">The proxy refused the tunnel or answered with garbage.</exception>
    /// <exception cref="SocketException">The proxy is unreachable.</exception>
    internal async Task<Socket> ConnectAsync(IPAddress target, int port, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            throw new InvalidOperationException("No upstream proxy is configured.");

        var socket = await ConnectToProxyAsync(cancellationToken);
        try
        {
            await using var stream = new NetworkStream(socket, ownsSocket: false);
            if (_scheme == "http")
                await HttpConnectAsync(stream, target, port, cancellationToken);
            else
                await Socks5ConnectAsync(stream, target, port, cancellationToken);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task<Socket> ConnectToProxyAsync(CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(_host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(_host!, cancellationToken);

        SocketException? lastError = null;
        foreach (var address in addresses)
        {
            // Per-family socket, as in PinnedConnector.
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, _port), cancellationToken);
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

        throw lastError ?? new SocketException((int)SocketError.HostNotFound);
    }

    private async Task HttpConnectAsync(NetworkStream stream, IPAddress target, int port, CancellationToken ct)
    {
        // Always CONNECT, also for http:// targets: absolute-form forwarding would hand the proxy the
        // hostname. Some proxies allow CONNECT only to port 443; an http:// store then fails here.
        var authority = new IPEndPoint(target, port).ToString();
        var head = "CONNECT " + authority + " HTTP/1.1\r\nHost: " + authority + "\r\n";
        if (_credentials is var (user, password))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password));
            head += "Proxy-Authorization: Basic " + token + "\r\n";
        }
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head + "\r\n"), ct);

        // Read byte by byte up to the blank line: anything after it already belongs to the tunnel.
        var response = new List<byte>();
        var one = new byte[1];
        while (!EndsWithBlankLine(response))
        {
            if (response.Count >= MaxResponseHeadBytes)
                throw new UpstreamProxyException("The upstream proxy sent an oversized response.");
            if (await stream.ReadAsync(one, ct) == 0)
                throw new UpstreamProxyException("The upstream proxy closed the connection during CONNECT.");
            response.Add(one[0]);
        }

        var statusLine = Encoding.ASCII.GetString(response.ToArray()).Split("\r\n", 2)[0];
        var parts = statusLine.Split(' ', 3);
        if (parts.Length < 2 || !parts[0].StartsWith("HTTP/1.", StringComparison.Ordinal) || !int.TryParse(parts[1], out var status))
            throw new UpstreamProxyException("The upstream proxy sent an invalid CONNECT response.");
        if (status is < 200 or > 299)
            throw new UpstreamProxyException($"The upstream proxy refused the tunnel (HTTP {status}).");
    }

    private static bool EndsWithBlankLine(List<byte> bytes) =>
        bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n';

    private async Task Socks5ConnectAsync(NetworkStream stream, IPAddress target, int port, CancellationToken ct)
    {
        byte[] greeting = _credentials is null ? [0x05, 0x01, 0x00] : [0x05, 0x02, 0x00, 0x02];
        await stream.WriteAsync(greeting, ct);
        var choice = await ReadAsync(stream, 2, ct);
        if (choice[0] != 0x05)
            throw new UpstreamProxyException("The upstream proxy is not a SOCKS5 proxy.");

        if (choice[1] == 0x02 && _credentials is var (user, password))
        {
            var userBytes = Encoding.UTF8.GetBytes(user);
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            if (userBytes.Length > 255 || passwordBytes.Length > 255)
                throw new UpstreamProxyException("The upstream proxy credentials are too long for SOCKS5.");
            await stream.WriteAsync(
                (byte[])[0x01, (byte)userBytes.Length, .. userBytes, (byte)passwordBytes.Length, .. passwordBytes], ct);
            var auth = await ReadAsync(stream, 2, ct);
            if (auth[1] != 0x00)
                throw new UpstreamProxyException("The upstream proxy rejected the credentials.");
        }
        else if (choice[1] != 0x00)
        {
            throw new UpstreamProxyException("The upstream proxy accepts none of the offered auth methods.");
        }

        // CONNECT to an IP address (ATYP 1 or 4), never a domain name (ATYP 3).
        var addressType = target.AddressFamily == AddressFamily.InterNetwork ? (byte)0x01 : (byte)0x04;
        var portBytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(portBytes, (ushort)port);
        await stream.WriteAsync((byte[])[0x05, 0x01, 0x00, addressType, .. target.GetAddressBytes(), .. portBytes], ct);

        var reply = await ReadAsync(stream, 4, ct);
        var boundLength = reply[3] switch
        {
            0x01 => 4,
            0x04 => 16,
            0x03 => (await ReadAsync(stream, 1, ct))[0],
            _ => throw new UpstreamProxyException("The upstream proxy sent an invalid SOCKS5 reply.")
        };
        await ReadAsync(stream, boundLength + 2, ct);
        if (reply[1] != 0x00)
            throw new UpstreamProxyException($"The upstream proxy refused the tunnel (SOCKS5 reply {reply[1]}).");
    }

    private static async Task<byte[]> ReadAsync(NetworkStream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        try
        {
            await stream.ReadExactlyAsync(buffer, ct);
        }
        catch (EndOfStreamException)
        {
            throw new UpstreamProxyException("The upstream proxy closed the connection during the handshake.");
        }
        return buffer;
    }
}

/// <summary>The upstream proxy refused a tunnel. The message never carries the proxy URL or credentials.</summary>
public sealed class UpstreamProxyException(string message) : IOException(message);
