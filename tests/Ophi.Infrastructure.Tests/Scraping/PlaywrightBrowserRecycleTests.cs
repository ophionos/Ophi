using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Playwright;
using Moq;
using Ophi.Infrastructure.Net;
using Ophi.Infrastructure.Scraping;

namespace Ophi.Infrastructure.Tests.Scraping;

/// <summary>
/// #132: the Playwright node driver grows for as long as it lives, so the manager replaces the
/// driver and its browser after a number of contexts or an age, once no context is open.
/// </summary>
public class PlaywrightBrowserRecycleTests : IAsyncDisposable
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-26T00:00:00Z"));
    private readonly List<Generation> _generations = [];
    private PlaywrightBrowserManager? _manager;

    /// <summary>One driver + browser, as the factory hands them out.</summary>
    private sealed class Generation
    {
        public Mock<IPlaywright> Playwright { get; } = new();
        public Mock<IBrowser> Browser { get; } = new();
        public List<Mock<IBrowserContext>> Contexts { get; } = [];
        public bool FailNextContext { get; set; }
    }

    private PlaywrightBrowserManager CreateManager(int maxContexts = 100, TimeSpan? maxAge = null)
    {
        _manager = new PlaywrightBrowserManager(
            NullLogger<PlaywrightBrowserManager>.Instance,
            startProxy: () => PinnedSocksProxy.Start(),
            timeProvider: _time,
            createPlaywright: () => Task.FromResult(NewGeneration().Playwright.Object),
            maxContextsPerBrowser: maxContexts,
            maxBrowserAge: maxAge ?? TimeSpan.FromDays(1));
        return _manager;
    }

    private Generation NewGeneration()
    {
        var gen = new Generation();
        var chromium = new Mock<IBrowserType>();
        chromium.Setup(c => c.LaunchAsync(It.IsAny<BrowserTypeLaunchOptions>())).ReturnsAsync(gen.Browser.Object);
        gen.Playwright.Setup(p => p.Chromium).Returns(chromium.Object);
        gen.Browser.Setup(b => b.Version).Returns("test");
        gen.Browser.Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions>())).ReturnsAsync(() =>
        {
            if (gen.FailNextContext)
            {
                gen.FailNextContext = false;
                throw new PlaywrightException("Target closed");
            }
            var context = new Mock<IBrowserContext>();
            var page = new Mock<IPage>();
            page.Setup(p => p.Context).Returns(context.Object);
            context.Setup(c => c.NewPageAsync()).ReturnsAsync(page.Object);
            gen.Contexts.Add(context);
            return context.Object;
        });
        _generations.Add(gen);
        return gen;
    }

    private static void Close(Mock<IBrowserContext> context) =>
        context.Raise(c => c.Close += null, context.Object, context.Object);

    private async Task OpenAndCloseAsync(PlaywrightBrowserManager manager, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await manager.NewPageAsync();
            Close(_generations[^1].Contexts[^1]);
        }
    }

    [Fact]
    public async Task NewPageAsync_AfterMaxContexts_StartsAFreshDriverAndBrowser()
    {
        var manager = CreateManager(maxContexts: 2);
        await OpenAndCloseAsync(manager, 2);

        await manager.NewPageAsync();

        _generations.Should().HaveCount(2);
        _generations[0].Browser.Verify(b => b.CloseAsync(It.IsAny<BrowserCloseOptions>()), Times.Once);
        _generations[0].Playwright.Verify(p => p.Dispose(), Times.Once);
        _generations[1].Contexts.Should().HaveCount(1);
    }

    [Fact]
    public async Task NewPageAsync_BeforeTheLimits_KeepsTheSameBrowser()
    {
        var manager = CreateManager(maxContexts: 3);
        await OpenAndCloseAsync(manager, 2);

        await manager.NewPageAsync();

        _generations.Should().HaveCount(1);
        _generations[0].Contexts.Should().HaveCount(3);
    }

    [Fact]
    public async Task NewPageAsync_AfterMaxAge_StartsAFreshDriverAndBrowser()
    {
        var manager = CreateManager(maxAge: TimeSpan.FromHours(6));
        await OpenAndCloseAsync(manager, 1);
        _time.Advance(TimeSpan.FromHours(6));

        await manager.NewPageAsync();

        _generations.Should().HaveCount(2);
        _generations[0].Playwright.Verify(p => p.Dispose(), Times.Once);
    }

    [Fact]
    public async Task NewPageAsync_WhileAContextIsOpen_DoesNotRecycleEvenWhenDue()
    {
        // Closing the browser would kill the open scrape (or a user's challenge session).
        var manager = CreateManager(maxContexts: 1);
        await manager.NewPageAsync();

        await manager.NewPageAsync();

        _generations.Should().HaveCount(1);
        _generations[0].Browser.Verify(b => b.CloseAsync(It.IsAny<BrowserCloseOptions>()), Times.Never);
    }

    [Fact]
    public async Task NewPageAsync_AfterTheLastOpenContextCloses_RecyclesTheDueBrowser()
    {
        var manager = CreateManager(maxContexts: 1);
        await manager.NewPageAsync();
        await manager.NewPageAsync();
        Close(_generations[0].Contexts[0]);
        Close(_generations[0].Contexts[1]);

        await manager.NewPageAsync();

        _generations.Should().HaveCount(2);
    }

    [Fact]
    public async Task NewPageAsync_CloseFromABrowserReplacedAfterACrash_DoesNotCountForTheNewBrowser()
    {
        var manager = CreateManager(maxContexts: 1);
        await manager.NewPageAsync();                       // gen 1, context A stays open
        _generations[0].FailNextContext = true;
        var crash = async () => await manager.NewPageAsync();
        await crash.Should().ThrowAsync<PlaywrightException>(); // crash: gen 1 is reset
        await manager.NewPageAsync();                       // gen 2, context B stays open, now due

        Close(_generations[0].Contexts[0]);                 // late close of A, from gen 1
        await manager.NewPageAsync();

        // B is still open, so gen 2 must not be recycled.
        _generations.Should().HaveCount(2);
        _generations[1].Browser.Verify(b => b.CloseAsync(It.IsAny<BrowserCloseOptions>()), Times.Never);
    }

    [Fact]
    public async Task Container_BuildsTheManagerWithItsOptionalRecycleParameters()
    {
        // Same registration as DependencyInjection.AddInfrastructure: the container must fill the
        // registered services and fall back to the defaults for the rest.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<IPlaywrightBrowserManager, PlaywrightBrowserManager>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        provider.GetRequiredService<IPlaywrightBrowserManager>().Should().BeOfType<PlaywrightBrowserManager>();
    }

    public async ValueTask DisposeAsync()
    {
        if (_manager != null) await _manager.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
