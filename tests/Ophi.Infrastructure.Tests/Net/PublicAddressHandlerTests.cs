using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Ophi.Infrastructure.Net;

namespace Ophi.Infrastructure.Tests.Net;

/// <summary>
/// The handler resolves each host itself, drops blocked addresses, and connects only to an address it
/// checked. The resolver and policy are injected, so no test touches real DNS.
/// </summary>
public class PublicAddressHandlerTests
{
    private static HttpClient ClientFor(Dictionary<string, IPAddress[]> dns, Func<IPAddress, bool> isBlocked) =>
        new(PublicAddressHandler.Create(
            (host, _) => Task.FromResult(dns.TryGetValue(host, out var addresses) ? addresses : []),
            isBlocked));

    [Fact]
    public async Task Send_HostResolvingToPrivateAddress_ThrowsBlockedWithoutTheAddress()
    {
        using var client = ClientFor(
            new() { ["evil.test"] = [IPAddress.Parse("10.0.0.1")] }, AddressPolicy.IsBlocked);

        var act = () => client.GetAsync("http://evil.test/", TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        PublicAddressHandler.IsBlockedDestination(ex).Should().BeTrue();
        ex.ToString().Should().NotContain("10.0.0.1");
    }

    [Fact]
    public async Task Send_HostWithNoAddresses_ThrowsBlocked()
    {
        using var client = ClientFor(new(), AddressPolicy.IsBlocked);

        var act = () => client.GetAsync("http://nowhere.test/", TestContext.Current.CancellationToken);

        PublicAddressHandler.IsBlockedDestination((await act.Should().ThrowAsync<HttpRequestException>()).Which)
            .Should().BeFalse("an unresolvable name is a network error, not a blocked destination");
    }

    [Fact]
    public async Task Send_MixedAnswer_ConnectsOnlyToTheAllowedAddress()
    {
        await using var server = await LoopbackServer.StartAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
        using var client = ClientFor(
            new() { ["mixed.test"] = [IPAddress.Parse("10.0.0.1"), IPAddress.Loopback] },
            ip => !IPAddress.IsLoopback(ip));

        var body = await client.GetStringAsync($"http://mixed.test:{server.Port}/", TestContext.Current.CancellationToken);

        body.Should().Be("ok");
    }

    [Fact]
    public async Task Send_RedirectToBlockedHost_IsCheckedOnTheNextHop()
    {
        // The first hop is allowed; its 302 points at a name that resolves to a blocked address. The
        // handler follows redirects itself, and each new connection goes through the same check.
        await using var server = await LoopbackServer.StartAsync(
            "HTTP/1.1 302 Found\r\nLocation: http://inner.test/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        var inner = IPAddress.Parse("127.0.0.2");
        using var client = ClientFor(
            new() { ["start.test"] = [IPAddress.Loopback], ["inner.test"] = [inner] },
            ip => ip.Equals(inner));

        var act = () => client.GetAsync($"http://start.test:{server.Port}/", TestContext.Current.CancellationToken);

        PublicAddressHandler.IsBlockedDestination((await act.Should().ThrowAsync<HttpRequestException>()).Which)
            .Should().BeTrue();
        server.Connections.Should().Be(1);
    }

    [Fact]
    public void Create_DisablesTheProxy()
    {
        // With a proxy the connect callback would check the proxy's address, not the target's.
        using var handler = (SocketsHttpHandler)PublicAddressHandler.Create();

        handler.UseProxy.Should().BeFalse();
        handler.ConnectCallback.Should().NotBeNull();
    }
}
