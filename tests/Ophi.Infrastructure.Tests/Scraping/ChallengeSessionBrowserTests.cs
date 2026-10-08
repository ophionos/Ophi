using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Infrastructure.Net;
using Ophi.Infrastructure.Scraping;
using Ophi.Infrastructure.Tests.Net;

namespace Ophi.Infrastructure.Tests.Scraping;

/// <summary>
/// A real Chromium against a fake challenge on 127.0.0.1: the page has a challenge title and a button
/// that sets a cookie and reloads. Proves the whole loop the API drives — forwarded input clears the
/// page, the saved state carries the cookie, and a new context from that state passes the challenge.
/// </summary>
[Trait("Category", "Integration")]
public class ChallengeSessionBrowserTests
{
    private const string Challenge =
        "<html><head><title>Just a moment...</title></head><body style=\"margin:0\">" +
        "<button style=\"position:absolute;left:0;top:0;width:400px;height:400px\" " +
        "onclick=\"document.cookie='cleared=1; path=/; max-age=3600'; location.reload()\">Verify</button>" +
        "</body></html>";

    private const string Product = "<html><head><title>Product</title></head><body>$5.00</body></html>";

    private static string Respond(string head)
    {
        var body = head.Contains("cookie: cleared=1", StringComparison.OrdinalIgnoreCase) ? Product : Challenge;
        return $"HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
    }

    [Fact]
    public async Task Session_UserClicksTheChallenge_SavedStatePassesInANewContext()
    {
        await using var store = await LoopbackServer.StartAsync(Respond);
        var url = $"http://127.0.0.1:{store.Port}/product";
        await using var browsers = new PlaywrightBrowserManager(
            NullLogger<PlaywrightBrowserManager>.Instance,
            startProxy: () => PinnedSocksProxy.Start(isBlocked: _ => false));
        await using var sessions = new ChallengeSessionManager(
            true, browsers, TimeProvider.System, NullLogger<ChallengeSessionManager>.Instance);

        var session = await sessions.StartAsync(Guid.NewGuid(), url, TestContext.Current.CancellationToken);
        (await session.IsClearedAsync()).Should().BeFalse();
        (await session.ScreenshotAsync()).Take(2).Should().Equal(0xFF, 0xD8); // JPEG

        await session.PointerAsync(down: true, 100, 100);
        await session.PointerAsync(down: false, 100, 100);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!await session.IsClearedAsync())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the click should clear the challenge");
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
        var state = await session.StorageStateAsync();
        state.Should().Contain("\"cleared\"");

        var page = await browsers.NewPageAsync(session.UserAgent, state);
        await page.GotoAsync(url);

        (await page.TitleAsync()).Should().Be("Product");
        store.Requests.Last().Should().Contain("User-Agent: " + session.UserAgent);
        await page.Context.CloseAsync();
    }
}
