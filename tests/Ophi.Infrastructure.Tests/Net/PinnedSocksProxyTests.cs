using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Ophi.Infrastructure.Net;

namespace Ophi.Infrastructure.Tests.Net;

public class PinnedSocksProxyTests
{
    private static readonly IPAddress Blocked = IPAddress.Parse("127.0.0.2");

    private static PinnedSocksProxy StartProxy(params IPAddress[] answer) =>
        PinnedSocksProxy.Start(
            resolve: (_, _) => Task.FromResult(answer),
            isBlocked: ip => ip.Equals(Blocked) || ip.Equals(IPAddress.IPv6Loopback));

    private static async Task<NetworkStream> GreetAsync(PinnedSocksProxy proxy, CancellationToken ct)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port, ct);
        var stream = client.GetStream();
        await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, ct);
        var reply = new byte[2];
        await stream.ReadExactlyAsync(reply, ct);
        reply.Should().Equal(0x05, 0x00);
        return stream;
    }

    private static byte[] DomainRequest(string host, int port, byte command = 0x01) =>
        [0x05, command, 0x00, 0x03, (byte)host.Length, .. Encoding.ASCII.GetBytes(host), (byte)(port >> 8), (byte)port];

    private static byte[] AddressRequest(IPAddress address, int port) =>
        [0x05, 0x01, 0x00, address.AddressFamily == AddressFamily.InterNetwork ? (byte)0x01 : (byte)0x04,
         .. address.GetAddressBytes(), (byte)(port >> 8), (byte)port];

    /// <summary>Reads the reply header and returns its REP code (the rest of the reply is drained).</summary>
    private static async Task<byte> ReadReplyAsync(NetworkStream stream, CancellationToken ct)
    {
        var head = new byte[4];
        await stream.ReadExactlyAsync(head, ct);
        head[0].Should().Be(0x05);
        var rest = head[3] switch { 0x01 => 4 + 2, 0x04 => 16 + 2, _ => throw new InvalidDataException() };
        await stream.ReadExactlyAsync(new byte[rest], ct);
        return head[1];
    }

    [Fact]
    public async Task Connect_HostResolvesToAllowedAddress_RelaysTheConnection()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await LoopbackServer.StartAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
        await using var proxy = StartProxy(IPAddress.Loopback);
        await using var stream = await GreetAsync(proxy, ct);

        await stream.WriteAsync(DomainRequest("shop.example", server.Port), ct);
        (await ReadReplyAsync(stream, ct)).Should().Be(0x00);

        await stream.WriteAsync("GET / HTTP/1.1\r\nHost: shop.example\r\n\r\n"u8.ToArray(), ct);
        var response = await new StreamReader(stream).ReadToEndAsync(ct);
        response.Should().StartWith("HTTP/1.1 200 OK").And.EndWith("ok");
    }

    [Fact]
    public async Task Connect_HostResolvesToBlockedAddress_RepliesNotAllowedWithoutConnecting()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var proxy = StartProxy(Blocked);
        await using var stream = await GreetAsync(proxy, ct);

        await stream.WriteAsync(DomainRequest("rebind.example", 80), ct);

        (await ReadReplyAsync(stream, ct)).Should().Be(PinnedSocksProxy.ReplyNotAllowed);
    }

    [Fact]
    public async Task Connect_MixedAnswer_ConnectsToTheAllowedAddress()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await LoopbackServer.StartAsync(
            "HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n");
        await using var proxy = StartProxy(Blocked, IPAddress.Loopback);
        await using var stream = await GreetAsync(proxy, ct);

        await stream.WriteAsync(DomainRequest("mixed.example", server.Port), ct);

        (await ReadReplyAsync(stream, ct)).Should().Be(0x00);
    }

    [Fact]
    public async Task Connect_BlockedIPv4Literal_RepliesNotAllowed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var proxy = StartProxy();
        await using var stream = await GreetAsync(proxy, ct);

        await stream.WriteAsync(AddressRequest(Blocked, 80), ct);

        (await ReadReplyAsync(stream, ct)).Should().Be(PinnedSocksProxy.ReplyNotAllowed);
    }

    [Fact]
    public async Task Connect_BlockedIPv6Literal_RepliesNotAllowed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var proxy = StartProxy();
        await using var stream = await GreetAsync(proxy, ct);

        await stream.WriteAsync(AddressRequest(IPAddress.IPv6Loopback, 80), ct);

        (await ReadReplyAsync(stream, ct)).Should().Be(PinnedSocksProxy.ReplyNotAllowed);
    }

    [Fact]
    public async Task Connect_NameDoesNotResolve_RepliesHostUnreachable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var proxy = StartProxy();
        await using var stream = await GreetAsync(proxy, ct);

        await stream.WriteAsync(DomainRequest("nowhere.example", 80), ct);

        (await ReadReplyAsync(stream, ct)).Should().Be(0x04);
    }

    [Fact]
    public async Task Request_NotConnect_RepliesCommandNotSupported()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var proxy = StartProxy(IPAddress.Loopback);
        await using var stream = await GreetAsync(proxy, ct);

        await stream.WriteAsync(DomainRequest("shop.example", 80, command: 0x02), ct);

        (await ReadReplyAsync(stream, ct)).Should().Be(0x07);
    }

    [Fact]
    public async Task Greeting_WithoutNoAuthMethod_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var proxy = StartProxy(IPAddress.Loopback);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port, ct);
        var stream = client.GetStream();

        await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x02 }, ct);

        var reply = new byte[2];
        await stream.ReadExactlyAsync(reply, ct);
        reply.Should().Equal(0x05, 0xFF);
    }

    [Fact]
    public async Task Start_ListensOnLoopbackOnly()
    {
        await using var proxy = StartProxy();

        proxy.Server.Should().Be($"socks5://127.0.0.1:{proxy.Port}");
        proxy.Port.Should().BePositive();
    }
}
