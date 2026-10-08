using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Ophi.Infrastructure.Scraping;

/// <summary>
/// Remote browser sessions in which a user solves a store's anti-bot challenge by hand: the API streams
/// screenshots of a server-side page and forwards the user's pointer and keyboard input to it. When the
/// page is past the challenge, its cookies are saved as a <see cref="Domain.Entities.StoreClearance"/>
/// for later browser scrapes. Off unless the operator sets <see cref="EnabledKey"/> and the process has
/// a browser.
/// </summary>
public interface IChallengeSessionManager
{
    bool IsEnabled { get; }

    /// <summary>Opens <paramref name="url"/> in a new session for the user, closing the user's earlier one.</summary>
    /// <exception cref="InvalidOperationException">The feature is off.</exception>
    /// <exception cref="ChallengeLimitException">Too many users have a session open.</exception>
    Task<IChallengeSession> StartAsync(Guid userId, string url, CancellationToken cancellationToken);

    /// <summary>The user's live session, or null. Counts as activity.</summary>
    IChallengeSession? Find(Guid userId);

    Task CloseAsync(Guid userId);
}

public interface IChallengeSession
{
    /// <summary>Lower-case host of the URL the session opened; the clearance is saved for this host.</summary>
    string Host { get; }

    /// <summary>The page's User-Agent; later scrapes must send the same one for the cookies to pass.</summary>
    string UserAgent { get; }

    Task<byte[]> ScreenshotAsync();

    /// <summary>Moves the mouse to the viewport point and presses (<paramref name="down"/>) or releases it.</summary>
    Task PointerAsync(bool down, double x, double y);

    Task TypeAsync(string text);

    Task PressAsync(string key);

    /// <summary>True when the page has finished loading and is not a challenge page.</summary>
    Task<bool> IsClearedAsync();

    Task<string> StorageStateAsync();
}

public sealed class ChallengeLimitException() : Exception("Too many challenge sessions are open. Try again in a few minutes.");

public sealed class ChallengeSessionManager : IChallengeSessionManager, IAsyncDisposable
{
    public const string EnabledKey = "Scraping:InteractiveChallenge:Enabled";

    /// <summary>Each session is a browser context (tens of MB); the cap bounds the API's memory.</summary>
    public const int MaxSessions = 3;

    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan HardTimeout = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    private readonly IPlaywrightBrowserManager? _browsers;
    private readonly TimeProvider _time;
    private readonly ILogger<ChallengeSessionManager> _logger;
    private readonly Dictionary<Guid, ChallengeSession> _sessions = [];
    private readonly Lock _lock = new();
    private readonly ITimer? _sweep;

    public ChallengeSessionManager(bool enabled, IPlaywrightBrowserManager? browsers, TimeProvider time, ILogger<ChallengeSessionManager> logger)
    {
        _browsers = browsers;
        _time = time;
        _logger = logger;
        IsEnabled = enabled && browsers != null;
        // A user who closes the tab sends no DELETE; the sweep closes the session's browser context.
        if (IsEnabled)
            _sweep = time.CreateTimer(_ => Sweep(), null, SweepInterval, SweepInterval);
    }

    public bool IsEnabled { get; }

    public async Task<IChallengeSession> StartAsync(Guid userId, string url, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            throw new InvalidOperationException("Challenge solving is not enabled on this server.");

        ChallengeSession? previous;
        lock (_lock)
        {
            _sessions.Remove(userId, out previous);
            if (_sessions.Count >= MaxSessions)
                throw new ChallengeLimitException();
        }
        if (previous != null)
            await previous.CloseAsync();

        var page = await _browsers!.NewPageAsync();
        try
        {
            try
            {
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30000 });
            }
            catch (TimeoutException)
            {
                // A challenge page that never fires "load" can still be shown and solved.
            }
            cancellationToken.ThrowIfCancellationRequested();

            var userAgent = await page.EvaluateAsync<string>("() => navigator.userAgent");
            var now = _time.GetUtcNow();
            var session = new ChallengeSession(page, new Uri(url).Host.ToLowerInvariant(), userAgent, now);

            lock (_lock)
            {
                _sessions.Remove(userId, out previous);
                _sessions[userId] = session;
            }
            if (previous != null)
                await previous.CloseAsync();

            _logger.LogInformation("Challenge session opened for {Host}", session.Host);
            return session;
        }
        catch
        {
            await SafeCloseAsync(page.Context);
            throw;
        }
    }

    public IChallengeSession? Find(Guid userId)
    {
        var now = _time.GetUtcNow();
        ChallengeSession? session;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(userId, out session))
                return null;
            if (IsExpired(session, now))
            {
                _sessions.Remove(userId);
            }
            else
            {
                session.LastActivity = now;
                return session;
            }
        }
        _ = session.CloseAsync();
        return null;
    }

    public async Task CloseAsync(Guid userId)
    {
        ChallengeSession? session;
        lock (_lock)
            _sessions.Remove(userId, out session);
        if (session != null)
            await session.CloseAsync();
    }

    private static bool IsExpired(ChallengeSession session, DateTimeOffset now) =>
        now - session.LastActivity >= IdleTimeout || now - session.StartedAt >= HardTimeout;

    private void Sweep()
    {
        var now = _time.GetUtcNow();
        List<ChallengeSession> expired = [];
        lock (_lock)
        {
            foreach (var (userId, session) in _sessions.ToList())
            {
                if (!IsExpired(session, now))
                    continue;
                _sessions.Remove(userId);
                expired.Add(session);
            }
        }
        foreach (var session in expired)
            _ = session.CloseAsync();
    }

    private static async Task SafeCloseAsync(IBrowserContext context)
    {
        try { await context.CloseAsync(); } catch { /* already closed, or the browser is gone */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_sweep != null)
            await _sweep.DisposeAsync();
        List<ChallengeSession> open;
        lock (_lock)
        {
            open = [.. _sessions.Values];
            _sessions.Clear();
        }
        foreach (var session in open)
            await session.CloseAsync();
    }
}

public sealed class ChallengeSession : IChallengeSession
{
    public const int MaxTextLength = 256;

    /// <summary>Matches the context viewport set by <see cref="PlaywrightBrowserManager"/>.</summary>
    public const int ViewportWidth = 1920;
    public const int ViewportHeight = 1080;

    /// <summary>
    /// Keys a challenge or a login form needs. No modifiers: shortcuts (Ctrl+L, Alt+Left) are browser
    /// controls, not page input.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "Enter", "Tab", "Backspace", "Delete", "Escape", "Space",
        "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"
    };

    private readonly IPage _page;

    /// <summary>Input arrives as separate requests; chaining keeps a press and its release in order.</summary>
    private readonly Lock _inputLock = new();
    private Task _inputTail = Task.CompletedTask;

    internal ChallengeSession(IPage page, string host, string userAgent, DateTimeOffset now)
    {
        _page = page;
        Host = host;
        UserAgent = userAgent;
        StartedAt = now;
        LastActivity = now;
    }

    public string Host { get; }
    public string UserAgent { get; }
    internal DateTimeOffset StartedAt { get; }
    internal DateTimeOffset LastActivity { get; set; }

    public Task<byte[]> ScreenshotAsync() =>
        _page.ScreenshotAsync(new PageScreenshotOptions { Type = ScreenshotType.Jpeg, Quality = 60 });

    public async Task PointerAsync(bool down, double x, double y)
    {
        if (x is < 0 or >= ViewportWidth || double.IsNaN(x))
            throw new ArgumentOutOfRangeException(nameof(x));
        if (y is < 0 or >= ViewportHeight || double.IsNaN(y))
            throw new ArgumentOutOfRangeException(nameof(y));

        await RunInputAsync(async () =>
        {
            await _page.Mouse.MoveAsync((float)x, (float)y);
            if (down)
                await _page.Mouse.DownAsync();
            else
                await _page.Mouse.UpAsync();
        });
    }

    public Task TypeAsync(string text)
    {
        if (text.Length > MaxTextLength)
            throw new ArgumentException($"Text is longer than {MaxTextLength} characters.", nameof(text));
        return RunInputAsync(() => _page.Keyboard.TypeAsync(text));
    }

    public Task PressAsync(string key)
    {
        if (!AllowedKeys.Contains(key))
            throw new ArgumentException("That key is not allowed.", nameof(key));
        return RunInputAsync(() => _page.Keyboard.PressAsync(key));
    }

    public async Task<bool> IsClearedAsync()
    {
        try
        {
            var state = await _page.EvaluateAsync<string[]>("() => [document.readyState, document.title]");
            // An empty title is a page between navigations, not a cleared one.
            return state is ["complete", { Length: > 0 } title]
                && !AntiBotSignals.IsChallenge(title, _page.Url);
        }
        catch (PlaywrightException)
        {
            // The page navigated while the script ran.
            return false;
        }
    }

    public Task<string> StorageStateAsync() => _page.Context.StorageStateAsync();

    private Task RunInputAsync(Func<Task> input)
    {
        lock (_inputLock)
        {
            // Runs after the previous input whether that one failed or not.
            _inputTail = _inputTail.ContinueWith(_ => input(), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            return _inputTail;
        }
    }

    internal async Task CloseAsync()
    {
        try { await _page.Context.CloseAsync(); } catch { /* already closed, or the browser is gone */ }
    }
}
