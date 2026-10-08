using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ophi.Infrastructure.Tests.Net;

/// <summary>A one-response-per-connection HTTP server on 127.0.0.1 (or another loopback address).</summary>
internal sealed class LoopbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _response;
    private Task? _loop;
    private int _connections;

    private LoopbackServer(string response, IPAddress address)
    {
        _response = response;
        _listener = new TcpListener(address, 0);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public int Connections => Volatile.Read(ref _connections);

    public static Task<LoopbackServer> StartAsync(string response, IPAddress? address = null)
    {
        var server = new LoopbackServer(response, address ?? IPAddress.Loopback);
        server._listener.Start();
        server._loop = server.AcceptLoopAsync();
        return Task.FromResult(server);
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var socket = await _listener.AcceptSocketAsync(_stop.Token);
                Interlocked.Increment(ref _connections);
                var buffer = new byte[4096];
                await socket.ReceiveAsync(buffer, SocketFlags.None, _stop.Token);
                await socket.SendAsync(Encoding.ASCII.GetBytes(_response), SocketFlags.None, _stop.Token);
                socket.Shutdown(SocketShutdown.Both);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        if (_loop is not null) await _loop;
        _stop.Dispose();
    }
}
