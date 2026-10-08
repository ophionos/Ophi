using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ophi.Infrastructure.Tests.Net;

/// <summary>
/// An upstream proxy on 127.0.0.1 that records each tunnel request, then acts as the target: it reads
/// the tunneled request once and answers with a fixed HTTP 200 "ok". Http mode speaks CONNECT; Socks5
/// mode speaks SOCKS5 with username/password auth (RFC 1929).
/// </summary>
internal sealed class FakeUpstreamProxy : IAsyncDisposable
{
    public enum Mode { Http, Socks5 }

    private const string TargetResponse = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Mode _mode;
    private readonly string _httpStatus;
    private Task? _loop;
    private int _connections;

    private FakeUpstreamProxy(Mode mode, string httpStatus)
    {
        _mode = mode;
        _httpStatus = httpStatus;
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public int Connections => Volatile.Read(ref _connections);

    /// <summary>Http mode: the request head of the last CONNECT (request line and headers).</summary>
    public string? LastConnectHead { get; private set; }

    /// <summary>Socks5 mode: the ATYP and address bytes of the last CONNECT request, and the credentials.</summary>
    public byte LastAddressType { get; private set; }
    public byte[]? LastAddress { get; private set; }
    public int LastPort { get; private set; }
    public string? LastCredentials { get; private set; }

    public static FakeUpstreamProxy Start(Mode mode, string httpStatus = "200 Connection established")
    {
        var proxy = new FakeUpstreamProxy(mode, httpStatus);
        proxy._listener.Start();
        proxy._loop = proxy.AcceptLoopAsync();
        return proxy;
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var socket = await _listener.AcceptSocketAsync(_stop.Token);
                Interlocked.Increment(ref _connections);
                _ = ServeAsync(socket);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    private async Task ServeAsync(Socket socket)
    {
        await using var stream = new NetworkStream(socket, ownsSocket: true);
        try
        {
            var tunnelOpen = _mode == Mode.Http ? await HttpConnectAsync(stream) : await Socks5ConnectAsync(stream);
            if (!tunnelOpen)
                return;

            var buffer = new byte[4096];
            await socket.ReceiveAsync(buffer, SocketFlags.None, _stop.Token);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(TargetResponse), _stop.Token);
            socket.Shutdown(SocketShutdown.Both);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException) { }
    }

    private async Task<bool> HttpConnectAsync(NetworkStream stream)
    {
        var head = new StringBuilder();
        var one = new byte[1];
        while (!head.ToString().EndsWith("\r\n\r\n"))
        {
            await stream.ReadExactlyAsync(one, _stop.Token);
            head.Append((char)one[0]);
        }

        LastConnectHead = head.ToString();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {_httpStatus}\r\n\r\n"), _stop.Token);
        return _httpStatus.StartsWith('2');
    }

    private async Task<bool> Socks5ConnectAsync(NetworkStream stream)
    {
        var ct = _stop.Token;
        var greeting = await ReadAsync(stream, 2, ct);
        var methods = await ReadAsync(stream, greeting[1], ct);
        if (!methods.Contains((byte)0x02))
        {
            await stream.WriteAsync(new byte[] { 0x05, 0xFF }, ct);
            return false;
        }
        await stream.WriteAsync(new byte[] { 0x05, 0x02 }, ct);

        // RFC 1929: VER ULEN UNAME PLEN PASSWD
        var version = await ReadAsync(stream, 2, ct);
        var user = Encoding.UTF8.GetString(await ReadAsync(stream, version[1], ct));
        var passwordLength = (await ReadAsync(stream, 1, ct))[0];
        var password = Encoding.UTF8.GetString(await ReadAsync(stream, passwordLength, ct));
        LastCredentials = $"{user}:{password}";
        await stream.WriteAsync(new byte[] { 0x01, 0x00 }, ct);

        var request = await ReadAsync(stream, 4, ct);
        LastAddressType = request[3];
        LastAddress = request[3] switch
        {
            0x01 => await ReadAsync(stream, 4, ct),
            0x04 => await ReadAsync(stream, 16, ct),
            _ => await ReadAsync(stream, (await ReadAsync(stream, 1, ct))[0], ct)
        };
        var port = await ReadAsync(stream, 2, ct);
        LastPort = (port[0] << 8) | port[1];

        await stream.WriteAsync(new byte[] { 0x05, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0 }, ct);
        return true;
    }

    private static async Task<byte[]> ReadAsync(NetworkStream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, ct);
        return buffer;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        if (_loop is not null) await _loop;
        _stop.Dispose();
    }
}
