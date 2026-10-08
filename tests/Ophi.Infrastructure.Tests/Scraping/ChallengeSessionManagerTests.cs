using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Playwright;
using Moq;
using Ophi.Infrastructure.Scraping;

namespace Ophi.Infrastructure.Tests.Scraping;

public class ChallengeSessionManagerTests
{
    private const string Url = "https://shop.example/product";
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly Mock<IPlaywrightBrowserManager> _browsers = new();
    private readonly List<Mock<IPage>> _pages = [];

    public ChallengeSessionManagerTests()
    {
        _browsers.Setup(x => x.NewPageAsync(It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(() => NewPage().Object);
    }

    private Mock<IPage> NewPage(string title = "Just a moment...", string readyState = "complete")
    {
        var context = new Mock<IBrowserContext>();
        context.Setup(x => x.CloseAsync(It.IsAny<BrowserContextCloseOptions>())).Returns(Task.CompletedTask);
        context.Setup(x => x.StorageStateAsync(It.IsAny<BrowserContextStorageStateOptions>())).ReturnsAsync("{\"cookies\":[]}");
        var page = new Mock<IPage>();
        page.Setup(x => x.Context).Returns(context.Object);
        page.Setup(x => x.Url).Returns(Url);
        page.Setup(x => x.Mouse).Returns(new Mock<IMouse>().Object);
        page.Setup(x => x.Keyboard).Returns(new Mock<IKeyboard>().Object);
        page.Setup(x => x.EvaluateAsync<string>("() => navigator.userAgent", null)).ReturnsAsync("Session UA");
        page.Setup(x => x.EvaluateAsync<string[]>(It.Is<string>(s => s.Contains("readyState")), null))
            .ReturnsAsync([readyState, title]);
        _pages.Add(page);
        return page;
    }

    private ChallengeSessionManager Manager(bool enabled = true) =>
        new(enabled, _browsers.Object, _time, NullLogger<ChallengeSessionManager>.Instance);

    private static Mock<IBrowserContext> ContextOf(Mock<IPage> page) => Mock.Get(page.Object.Context);

    [Fact]
    public void IsEnabled_FlagOffOrNoBrowser_IsFalse()
    {
        Manager(enabled: false).IsEnabled.Should().BeFalse();
        new ChallengeSessionManager(true, null, _time, NullLogger<ChallengeSessionManager>.Instance).IsEnabled.Should().BeFalse();
        Manager().IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_WhenDisabled_Throws()
    {
        var act = () => Manager(enabled: false).StartAsync(Guid.NewGuid(), Url, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _browsers.Verify(x => x.NewPageAsync(It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_Always_OpensTheUrlAndRecordsTheHostAndUserAgent()
    {
        var userId = Guid.NewGuid();
        var manager = Manager();

        var session = await manager.StartAsync(userId, "https://Shop.Example/product", TestContext.Current.CancellationToken);

        session.Host.Should().Be("shop.example");
        session.UserAgent.Should().Be("Session UA");
        manager.Find(userId).Should().BeSameAs(session);
        _pages[0].Verify(x => x.GotoAsync("https://Shop.Example/product", It.IsAny<PageGotoOptions>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_SecondTimeForTheSameUser_ClosesTheFirstSession()
    {
        var userId = Guid.NewGuid();
        var manager = Manager();
        var ct = TestContext.Current.CancellationToken;
        await manager.StartAsync(userId, Url, ct);

        var second = await manager.StartAsync(userId, Url, ct);

        ContextOf(_pages[0]).Verify(x => x.CloseAsync(It.IsAny<BrowserContextCloseOptions>()), Times.Once);
        manager.Find(userId).Should().BeSameAs(second);
    }

    [Fact]
    public async Task StartAsync_AtTheSessionLimit_ThrowsForANewUser()
    {
        var manager = Manager();
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < ChallengeSessionManager.MaxSessions; i++)
            await manager.StartAsync(Guid.NewGuid(), Url, ct);

        var act = () => manager.StartAsync(Guid.NewGuid(), Url, ct);

        await act.Should().ThrowAsync<ChallengeLimitException>();
    }

    [Fact]
    public async Task Find_AfterTheIdleTimeout_ReturnsNullAndClosesTheSession()
    {
        var userId = Guid.NewGuid();
        var manager = Manager();
        await manager.StartAsync(userId, Url, TestContext.Current.CancellationToken);

        _time.Advance(ChallengeSessionManager.IdleTimeout);

        manager.Find(userId).Should().BeNull();
        ContextOf(_pages[0]).Verify(x => x.CloseAsync(It.IsAny<BrowserContextCloseOptions>()), Times.Once);
    }

    [Fact]
    public async Task Find_WithActivity_ExpiresAtTheHardTimeout()
    {
        var userId = Guid.NewGuid();
        var manager = Manager();
        await manager.StartAsync(userId, Url, TestContext.Current.CancellationToken);
        var step = ChallengeSessionManager.IdleTimeout / 2;

        for (var elapsed = TimeSpan.Zero; elapsed + step < ChallengeSessionManager.HardTimeout; elapsed += step)
        {
            manager.Find(userId).Should().NotBeNull();
            _time.Advance(step);
        }
        _time.Advance(step);

        manager.Find(userId).Should().BeNull();
    }

    [Fact]
    public async Task Sweep_AbandonedSession_IsClosedWithoutAFind()
    {
        var manager = Manager();
        await manager.StartAsync(Guid.NewGuid(), Url, TestContext.Current.CancellationToken);

        _time.Advance(ChallengeSessionManager.IdleTimeout + ChallengeSessionManager.SweepInterval);

        ContextOf(_pages[0]).Verify(x => x.CloseAsync(It.IsAny<BrowserContextCloseOptions>()), Times.Once);
    }

    [Theory]
    [InlineData("F12")]
    [InlineData("Control+L")]
    [InlineData("Alt+F4")]
    public async Task PressAsync_KeyNotAllowed_Throws(string key)
    {
        var session = await Manager().StartAsync(Guid.NewGuid(), Url, TestContext.Current.CancellationToken);

        var act = () => session.PressAsync(key);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PressAsync_AllowedKey_PressesIt()
    {
        var session = await Manager().StartAsync(Guid.NewGuid(), Url, TestContext.Current.CancellationToken);

        await session.PressAsync("Enter");

        Mock.Get(_pages[0].Object.Keyboard).Verify(x => x.PressAsync("Enter", It.IsAny<KeyboardPressOptions>()), Times.Once);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    [InlineData(1920, 10)]
    [InlineData(10, 1080)]
    public async Task PointerAsync_OutsideTheViewport_Throws(double x, double y)
    {
        var session = await Manager().StartAsync(Guid.NewGuid(), Url, TestContext.Current.CancellationToken);

        var act = () => session.PointerAsync(down: true, x, y);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task PointerAsync_DownThenUp_MovesAndPressesTheMouse()
    {
        var session = await Manager().StartAsync(Guid.NewGuid(), Url, TestContext.Current.CancellationToken);
        var mouse = Mock.Get(_pages[0].Object.Mouse);

        await session.PointerAsync(down: true, 100, 200);
        await session.PointerAsync(down: false, 100, 200);

        mouse.Verify(x => x.MoveAsync(100, 200, It.IsAny<MouseMoveOptions>()), Times.Exactly(2));
        mouse.Verify(x => x.DownAsync(It.IsAny<MouseDownOptions>()), Times.Once);
        mouse.Verify(x => x.UpAsync(It.IsAny<MouseUpOptions>()), Times.Once);
    }

    [Fact]
    public async Task TypeAsync_TooLong_Throws()
    {
        var session = await Manager().StartAsync(Guid.NewGuid(), Url, TestContext.Current.CancellationToken);

        var act = () => session.TypeAsync(new string('a', ChallengeSession.MaxTextLength + 1));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("Just a moment...", "complete", false)]
    [InlineData("", "complete", false)]
    [InlineData("Product", "loading", false)]
    [InlineData("Product", "complete", true)]
    public async Task IsClearedAsync_ReadsTheTitleAndLoadState(string title, string readyState, bool expected)
    {
        _browsers.Setup(x => x.NewPageAsync(It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(() => NewPage(title, readyState).Object);
        var session = await Manager().StartAsync(Guid.NewGuid(), Url, TestContext.Current.CancellationToken);

        (await session.IsClearedAsync()).Should().Be(expected);
    }

    [Fact]
    public async Task IsClearedAsync_PageIsNavigating_IsFalse()
    {
        var session = await Manager().StartAsync(Guid.NewGuid(), Url, TestContext.Current.CancellationToken);
        _pages[0].Setup(x => x.EvaluateAsync<string[]>(It.IsAny<string>(), null))
            .ThrowsAsync(new PlaywrightException("Execution context was destroyed"));

        (await session.IsClearedAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task CloseAsync_ForTheUser_ClosesAndForgetsTheSession()
    {
        var userId = Guid.NewGuid();
        var manager = Manager();
        await manager.StartAsync(userId, Url, TestContext.Current.CancellationToken);

        await manager.CloseAsync(userId);

        manager.Find(userId).Should().BeNull();
        ContextOf(_pages[0]).Verify(x => x.CloseAsync(It.IsAny<BrowserContextCloseOptions>()), Times.Once);
    }
}
