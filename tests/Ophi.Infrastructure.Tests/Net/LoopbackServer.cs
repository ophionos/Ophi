using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ophi.Infrastructure.Tests.Net;

/// <summary>A one-response-per-connection HTTP server on 127.0.0.1 (or another loopback address).</summary>
internal sealed class LoopbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<string, string> _respond;
    private Task? _loop;
    private int _connections;

    private LoopbackServer(Func<string, string> respond, IPAddress address)
    {
        _respond = respond;
        _listener = new TcpListener(address, 0);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public int Connections => Volatile.Read(ref _connections);

    /// <summary>The request heads received, in order.</summary>
    public ConcurrentQueue<string> Requests { get; } = new();

    public static Task<LoopbackServer> StartAsync(string response, IPAddress? address = null) =>
        StartAsync(_ => response, address);

    /// <summary>Answers each request with <paramref name="respond"/>(request head).</summary>
    public static Task<LoopbackServer> StartAsync(Func<string, string> respond, IPAddress? address = null)
    {
        var server = new LoopbackServer(respond, address ?? IPAddress.Loopback);
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
                var read = await socket.ReceiveAsync(buffer, SocketFlags.None, _stop.Token);
                var head = Encoding.ASCII.GetString(buffer, 0, read);
                Requests.Enqueue(head);
                await socket.SendAsync(Encoding.ASCII.GetBytes(_respond(head)), SocketFlags.None, _stop.Token);
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
