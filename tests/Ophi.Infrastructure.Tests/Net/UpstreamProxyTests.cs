using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Ophi.Infrastructure.Net;

namespace Ophi.Infrastructure.Tests.Net;

/// <summary>
/// The upstream proxy for operator-listed scrape domains. The target is resolved and checked locally,
/// and the proxy is asked to tunnel to that checked address — it never sees the hostname, so it cannot
/// resolve the name to a different (internal) address.
/// </summary>
public class UpstreamProxyTests
{
    private static readonly IPAddress Target = IPAddress.Parse("203.0.113.7");

    private static Task<IPAddress[]> Resolve(string host, CancellationToken _) =>
        Task.FromResult(host switch
        {
            "shop.test" or "www.shop.test" => new[] { Target },
            "evil.shop.test" => [IPAddress.Parse("10.0.0.1")],
            "direct.test" => [IPAddress.Loopback],
            _ => []
        });

    private static bool IsBlocked(IPAddress ip) => !ip.Equals(Target) && !IPAddress.IsLoopback(ip);

    private static UpstreamProxy Upstream(string url) => UpstreamProxy.FromConfiguration(url, "shop.test");

    private static HttpClient ClientThrough(UpstreamProxy upstream) =>
        new(PublicAddressHandler.Create(Resolve, IsBlocked, upstream));

    [Fact]
    public void FromConfiguration_BothEmpty_RoutesNothing()
    {
        var upstream = UpstreamProxy.FromConfiguration(null, " ");

        upstream.IsEnabled.Should().BeFalse();
        upstream.Routes("shop.test").Should().BeFalse();
    }

    [Theory]
    [InlineData("ftp://user:secret@proxy.test:21", "shop.test")]
    [InlineData("https://user:secret@proxy.test:443", "shop.test")]
    [InlineData("socks5://user:secret@proxy.test", "shop.test")]
    [InlineData("not a url secret", "shop.test")]
    [InlineData("http://user:secret@proxy.test:8080", "")]
    [InlineData("", "shop.test")]
    [InlineData("http://user:secret@proxy.test:8080", "shop.test/path")]
    [InlineData("http://user:secret@proxy.test:8080", "shop.test:443")]
    public void FromConfiguration_InvalidValue_ThrowsWithoutTheCredentials(string url, string domains)
    {
        var act = () => UpstreamProxy.FromConfiguration(url, domains);

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().NotContain("secret");
    }

    [Theory]
    [InlineData("shop.test", true)]
    [InlineData("www.shop.test", true)]
    [InlineData("WWW.Shop.Test.", true)]
    [InlineData("a.other.test", true)]
    [InlineData("notshop.test", false)]
    [InlineData("test", false)]
    [InlineData("203.0.113.7", false)]
    public void Routes_MatchesListedDomainsAndTheirSubdomains(string host, bool expected)
    {
        var upstream = UpstreamProxy.FromConfiguration("http://proxy.test:8080", "shop.test, .Other.Test");

        upstream.Routes(host).Should().Be(expected);
    }

    [Fact]
    public async Task HttpUpstream_TunnelsToTheCheckedAddressNotTheHostname()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var proxy = FakeUpstreamProxy.Start(FakeUpstreamProxy.Mode.Http);
        using var client = ClientThrough(Upstream($"http://user:p%40ss@127.0.0.1:{proxy.Port}"));

        var body = await client.GetStringAsync("http://www.shop.test/item", ct);

        body.Should().Be("ok");
        proxy.LastConnectHead.Should().StartWith("CONNECT 203.0.113.7:80 HTTP/1.1\r\n");
        proxy.LastConnectHead.Should().NotContain("shop.test");
        proxy.LastConnectHead.Should().Contain(
            "Proxy-Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("user:p@ss")));
    }

    [Fact]
    public async Task Socks5Upstream_TunnelsToTheCheckedAddressWithCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var proxy = FakeUpstreamProxy.Start(FakeUpstreamProxy.Mode.Socks5);
        using var client = ClientThrough(Upstream($"socks5://user:p%40ss@127.0.0.1:{proxy.Port}"));

        var body = await client.GetStringAsync("http://shop.test:8081/item", ct);

        body.Should().Be("ok");
        proxy.LastAddressType.Should().Be(0x01, "an IPv4 address, never a domain name");
        proxy.LastAddress.Should().Equal(Target.GetAddressBytes());
        proxy.LastPort.Should().Be(8081);
        proxy.LastCredentials.Should().Be("user:p@ss");
    }

    [Fact]
    public async Task RoutedHost_ResolvingToBlockedAddress_IsRefusedBeforeTheProxyIsContacted()
    {
        await using var proxy = FakeUpstreamProxy.Start(FakeUpstreamProxy.Mode.Http);
        using var client = ClientThrough(Upstream($"http://127.0.0.1:{proxy.Port}"));

        var act = () => client.GetAsync("http://evil.shop.test/", TestContext.Current.CancellationToken);

        PublicAddressHandler.IsBlockedDestination((await act.Should().ThrowAsync<HttpRequestException>()).Which)
            .Should().BeTrue();
        proxy.Connections.Should().Be(0);
    }

    [Fact]
    public async Task UnlistedHost_ConnectsDirectly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await LoopbackServer.StartAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 6\r\nConnection: close\r\n\r\ndirect");
        await using var proxy = FakeUpstreamProxy.Start(FakeUpstreamProxy.Mode.Http);
        using var client = ClientThrough(Upstream($"http://127.0.0.1:{proxy.Port}"));

        var body = await client.GetStringAsync($"http://direct.test:{server.Port}/", ct);

        body.Should().Be("direct");
        proxy.Connections.Should().Be(0);
    }

    [Fact]
    public async Task HttpUpstream_RefusesTheTunnel_FailsWithoutTheCredentials()
    {
        await using var proxy = FakeUpstreamProxy.Start(FakeUpstreamProxy.Mode.Http, "407 Proxy Authentication Required");
        using var client = ClientThrough(Upstream($"http://user:secret@127.0.0.1:{proxy.Port}"));

        var act = () => client.GetAsync("http://shop.test/", TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        ex.ToString().Should().Contain("407").And.NotContain("secret");
        PublicAddressHandler.IsBlockedDestination(ex).Should().BeFalse();
    }

    [Fact]
    public async Task PinnedSocksProxy_RoutedHost_TunnelsThroughTheUpstream()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var upstream = FakeUpstreamProxy.Start(FakeUpstreamProxy.Mode.Http);
        await using var socks = PinnedSocksProxy.Start(Resolve, IsBlocked, Upstream($"http://127.0.0.1:{upstream.Port}"));

        var reply = await SocksConnectAsync(socks, "shop.test", 443, ct);

        reply.Should().Be(0x00);
        upstream.LastConnectHead.Should().StartWith("CONNECT 203.0.113.7:443 HTTP/1.1\r\n");
    }

    [Fact]
    public async Task PinnedSocksProxy_UpstreamRefuses_RepliesGeneralFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var upstream = FakeUpstreamProxy.Start(FakeUpstreamProxy.Mode.Http, "403 Forbidden");
        await using var socks = PinnedSocksProxy.Start(Resolve, IsBlocked, Upstream($"http://127.0.0.1:{upstream.Port}"));

        var reply = await SocksConnectAsync(socks, "shop.test", 443, ct);

        reply.Should().Be(0x01);
    }

    private static async Task<byte> SocksConnectAsync(PinnedSocksProxy socks, string host, int port, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, socks.Port, ct);
        var stream = client.GetStream();
        await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, ct);
        await stream.ReadExactlyAsync(new byte[2], ct);
        await stream.WriteAsync(
            (byte[])[0x05, 0x01, 0x00, 0x03, (byte)host.Length, .. Encoding.ASCII.GetBytes(host), (byte)(port >> 8), (byte)port], ct);
        var head = new byte[10];
        await stream.ReadExactlyAsync(head, ct);
        return head[1];
    }
}
