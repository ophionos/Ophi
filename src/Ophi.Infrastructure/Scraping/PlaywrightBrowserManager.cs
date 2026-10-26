using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Ophi.Infrastructure.Net;

namespace Ophi.Infrastructure.Scraping;

/// <summary>
/// Manages the Playwright driver and browser as a singleton, reused across scraping requests.
/// Resets after a crash, and replaces both after <c>maxContextsPerBrowser</c> contexts or
/// <c>maxBrowserAge</c>, once no context is open (#132): the node driver grows for as long as it
/// lives (about 100 MiB/day on the worker), and only ending the process returns that memory.
/// </summary>
public sealed class PlaywrightBrowserManager(
    ILogger<PlaywrightBrowserManager> logger,
    UpstreamProxy? upstream = null,
    Func<PinnedSocksProxy>? startProxy = null,
    TimeProvider? timeProvider = null,
    Func<Task<IPlaywright>>? createPlaywright = null,
    int maxContextsPerBrowser = PlaywrightBrowserManager.DefaultMaxContextsPerBrowser,
    TimeSpan? maxBrowserAge = null) : IPlaywrightBrowserManager
{
    // At ~100 MiB/day of driver growth, 6 hours bounds it to about 25 MiB. A relaunch costs a second
    // or two, a few times a day. The context limit is a backstop for a burst of browser scrapes.
    public const int DefaultMaxContextsPerBrowser = 200;
    public static readonly TimeSpan DefaultMaxBrowserAge = TimeSpan.FromHours(6);

    private readonly Func<PinnedSocksProxy> _startProxy = startProxy ?? (() => PinnedSocksProxy.Start(upstream: upstream));
    private readonly Func<Task<IPlaywright>> _createPlaywright = createPlaywright ?? Playwright.CreateAsync;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _maxBrowserAge = maxBrowserAge ?? DefaultMaxBrowserAge;

    /// <summary>
    /// The contexts of one driver + browser. A context's Close event decrements the generation it
    /// was created in, so a late close from a replaced browser cannot touch the current count.
    /// </summary>
    private sealed class BrowserGeneration(DateTimeOffset startedAt)
    {
        public readonly DateTimeOffset StartedAt = startedAt;
        public int OpenContexts;
        public int ContextsCreated;
        public bool LoggedRecycleBlocked;
    }

    private IPlaywright? _playwright;
    private PinnedSocksProxy? _proxy;
    private IBrowser? _browser;
    private BrowserGeneration? _generation;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Gets a new page, in a new context, with realistic context settings. The caller closes the
    /// context. Initializes the browser if needed. Resets and reinitializes if the browser has crashed.
    /// </summary>
    public async Task<IPage> NewPageAsync(string? userAgent = null, string? storageState = null)
    {
        var (browser, generation) = await AcquireBrowserAsync();
        var closeTracked = false;

        try
        {
            var ua = userAgent ?? BrowserProfiles.GetRandom().UserAgent;

            // Create a new context with realistic settings to avoid bot detection
            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = ua,
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                Locale = "en-US",
                TimezoneId = "America/New_York",
                JavaScriptEnabled = true,
                StorageState = storageState
            });
            context.Close += (_, _) => Interlocked.Decrement(ref generation.OpenContexts);
            closeTracked = true;

            // Remove the webdriver flag that bot detectors check
            await context.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', { get: () => false });");

            return await context.NewPageAsync();
        }
        catch (Exception ex)
        {
            if (!closeTracked) Interlocked.Decrement(ref generation.OpenContexts);

            // Browser process may have crashed — reset so the next call gets a fresh browser
            logger.LogWarning(ex, "Browser page creation failed, resetting browser state for recovery");
            await ResetAsync(generation);
            throw;
        }
    }

    /// <summary>
    /// Under the lock: recycles a due, idle browser, initializes one if needed, and reserves a
    /// context slot. The browser is returned rather than read from the field later, because a crash
    /// reset by another caller can clear the field.
    /// </summary>
    private async Task<(IBrowser Browser, BrowserGeneration Generation)> AcquireBrowserAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_generation is { } current && RecycleReason(current) is { } reason)
            {
                var open = Volatile.Read(ref current.OpenContexts);
                if (open == 0)
                {
                    logger.LogInformation(
                        "Recycling Playwright driver and browser ({Reason}: {Contexts} contexts, {Age} old)",
                        reason, current.ContextsCreated, _time.GetUtcNow() - current.StartedAt);
                    await ResetCoreAsync();
                }
                else if (!current.LoggedRecycleBlocked)
                {
                    // A context that is never closed would block every recycle; make that visible.
                    current.LoggedRecycleBlocked = true;
                    logger.LogInformation(
                        "Playwright browser recycle is due ({Reason}) but waits for {Open} open contexts",
                        reason, open);
                }
            }

            if (_generation == null) await InitializeCoreAsync();

            var generation = _generation!;
            Interlocked.Increment(ref generation.OpenContexts);
            generation.ContextsCreated++;
            return (_browser!, generation);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private string? RecycleReason(BrowserGeneration generation) =>
        generation.ContextsCreated >= maxContextsPerBrowser ? "context limit"
        : _time.GetUtcNow() - generation.StartedAt >= _maxBrowserAge ? "age limit"
        : null;

    /// <summary>Starts the proxy, driver and browser. The caller holds the init lock.</summary>
    private async Task InitializeCoreAsync()
    {
        try
        {
            // SSRF guard: every connection the browser opens goes through the pinning proxy.
            _proxy = _startProxy();
            _playwright = await _createPlaywright();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                // "<-loopback>" removes Chromium's implicit loopback bypass, so localhost goes through
                // the proxy (and is refused) like every other host.
                Proxy = new Proxy { Server = _proxy.Server, Bypass = "<-loopback>" },
                Args =
                [
                    "--disable-blink-features=AutomationControlled",
                    "--disable-features=IsolateOrigins,site-per-process",
                    "--no-sandbox",
                    // WebRTC sends UDP directly, around the SOCKS proxy; this keeps it inside the proxy (TCP only).
                    "--force-webrtc-ip-handling-policy=disable_non_proxied_udp"
                ]
            });
            _generation = new BrowserGeneration(_time.GetUtcNow());

            logger.LogInformation("Playwright browser initialized (Chromium {Version})", _browser.Version);
        }
        catch
        {
            // A failed launch must not leave a driver or a listener behind; the next call starts fresh.
            _playwright?.Dispose();
            _playwright = null;
            _browser = null;
            if (_proxy != null)
            {
                await _proxy.DisposeAsync();
                _proxy = null;
            }
            throw;
        }
    }

    /// <summary>
    /// Resets browser state after a failure so the next call to NewPageAsync reinitializes from
    /// scratch. Skipped when another caller already replaced that generation.
    /// </summary>
    private async Task ResetAsync(BrowserGeneration failed)
    {
        await _initLock.WaitAsync();
        try
        {
            if (_generation == failed) await ResetCoreAsync();
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Closes the browser, ends the node driver (disposing <see cref="IPlaywright"/> stops it) and
    /// stops the proxy. The caller holds the init lock.
    /// </summary>
    private async Task ResetCoreAsync()
    {
        _generation = null;

        if (_browser != null)
        {
            try { await _browser.CloseAsync(); } catch { /* best effort on crashed browser */ }
            _browser = null;
        }

        if (_playwright != null)
        {
            try { _playwright.Dispose(); } catch { /* best effort */ }
            _playwright = null;
        }

        if (_proxy != null)
        {
            try { await _proxy.DisposeAsync(); } catch { /* best effort */ }
            _proxy = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_browser != null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;

        if (_proxy != null)
        {
            await _proxy.DisposeAsync();
            _proxy = null;
        }
        _generation = null;

        _initLock.Dispose();

        logger.LogInformation("Playwright browser disposed");
    }
}
