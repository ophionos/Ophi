using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Ophi.Infrastructure.Net;
using Ophi.Infrastructure.Scraping;

namespace Ophi.Infrastructure.Tests.Net;

/// <summary>
/// Runs a real Chromium through <see cref="PinnedSocksProxy"/>. Literal IPs, with a live listener at the
/// blocked target: if the proxy (or Chromium's proxy settings) let the connection through, the target
/// server would count it, so these tests fail on a guard that does nothing — not just on a refused port.
/// 127.0.0.1 is allowed; ::1 is blocked.
/// </summary>
[Trait("Category", "Integration")]
public class PinnedSocksProxyBrowserTests
{
    private const string Ok =
        "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: 13\r\nConnection: close\r\n\r\n<p>hello</p>\n";

    private static PlaywrightBrowserManager BrowserBlocking(params IPAddress[] blocked) => new(
        NullLogger<PlaywrightBrowserManager>.Instance,
        startProxy: () => PinnedSocksProxy.Start(isBlocked: blocked.Contains));

    private static string Redirect(int port) =>
        $"HTTP/1.1 302 Found\r\nLocation: http://[::1]:{port}/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";

    [Fact]
    public async Task Navigation_RedirectToBlockedAddress_NeverReachesTheTarget()
    {
        await using var target = await LoopbackServer.StartAsync(Ok, IPAddress.IPv6Loopback);
        await using var start = await LoopbackServer.StartAsync(Redirect(target.Port));
        await using var manager = BrowserBlocking(IPAddress.IPv6Loopback);
        var page = await manager.NewPageAsync();

        var act = () => page.GotoAsync($"http://127.0.0.1:{start.Port}/");

        (await act.Should().ThrowAsync<PlaywrightException>()).Which.Message.Should().Contain("ERR_SOCKS_CONNECTION_FAILED");
        start.Connections.Should().Be(1);
        target.Connections.Should().Be(0);
    }

    [Fact]
    public async Task Navigation_RedirectToAllowedAddress_ReachesTheTarget()
    {
        // Control for the test above: same redirect, nothing blocked.
        await using var target = await LoopbackServer.StartAsync(Ok, IPAddress.IPv6Loopback);
        await using var start = await LoopbackServer.StartAsync(Redirect(target.Port));
        await using var manager = BrowserBlocking();
        var page = await manager.NewPageAsync();

        var response = await page.GotoAsync($"http://127.0.0.1:{start.Port}/");

        response!.Status.Should().Be(200);
        target.Connections.Should().Be(1);
    }

    [Fact]
    public async Task Navigation_ListedDomain_TunnelsThroughTheUpstreamToTheCheckedAddress()
    {
        // Chromium sends the hostname to the pinning proxy; the upstream must get only the checked IP.
        await using var upstream = FakeUpstreamProxy.Start(FakeUpstreamProxy.Mode.Http);
        var route = UpstreamProxy.FromConfiguration($"http://127.0.0.1:{upstream.Port}", "shop.test");
        await using var manager = new PlaywrightBrowserManager(
            NullLogger<PlaywrightBrowserManager>.Instance,
            startProxy: () => PinnedSocksProxy.Start(
                resolve: (_, _) => Task.FromResult(new[] { IPAddress.Parse("203.0.113.7") }),
                isBlocked: _ => false,
                upstream: route));
        var page = await manager.NewPageAsync();

        var response = await page.GotoAsync("http://shop.test/item");

        response!.Status.Should().Be(200);
        upstream.LastConnectHead.Should().StartWith("CONNECT 203.0.113.7:80 HTTP/1.1\r\n");
    }

    [Fact]
    public async Task WebRtc_StunToAnyAddress_IsNotSentOutsideTheProxy()
    {
        // WebRTC UDP does not go through a SOCKS proxy; the launch flag must stop it entirely.
        var ct = TestContext.Current.CancellationToken;
        using var stun = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)stun.Client.LocalEndPoint!).Port;
        await using var manager = BrowserBlocking();
        var page = await manager.NewPageAsync();

        await page.EvaluateAsync(
            """
            async port => {
                const pc = new RTCPeerConnection({ iceServers: [{ urls: `stun:127.0.0.1:${port}` }] });
                pc.createDataChannel('x');
                await pc.setLocalDescription(await pc.createOffer());
                await new Promise(r => setTimeout(r, 3000));
                pc.close();
            }
            """, port);

        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(TimeSpan.FromSeconds(1));
        var received = async () => await stun.ReceiveAsync(wait.Token);
        await received.Should().ThrowAsync<OperationCanceledException>();
    }
}
