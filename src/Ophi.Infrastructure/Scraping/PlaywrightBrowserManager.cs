using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Ophi.Infrastructure.Net;

namespace Ophi.Infrastructure.Scraping;

/// <summary>
/// Manages the Playwright browser instance as a singleton.
/// Ensures the browser is created once and reused across all scraping requests.
/// Automatically resets and reinitializes if the browser process crashes.
/// </summary>
public sealed class PlaywrightBrowserManager(
    ILogger<PlaywrightBrowserManager> logger,
    Func<PinnedSocksProxy>? startProxy = null) : IPlaywrightBrowserManager
{
    private readonly Func<PinnedSocksProxy> _startProxy = startProxy ?? (() => PinnedSocksProxy.Start());

    private IPlaywright? _playwright;
    private PinnedSocksProxy? _proxy;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Gets a new page from the browser with realistic context settings.
    /// Initializes the browser if needed. Resets and reinitializes if the browser has crashed.
    /// </summary>
    public async Task<IPage> NewPageAsync(string? userAgent = null)
    {
        await EnsureInitializedAsync();

        try
        {
            var ua = userAgent ?? BrowserProfiles.GetRandom().UserAgent;

            // Create a new context with realistic settings to avoid bot detection
            var context = await _browser!.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = ua,
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                Locale = "en-US",
                TimezoneId = "America/New_York",
                JavaScriptEnabled = true
            });

            // Remove the webdriver flag that bot detectors check
            await context.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', { get: () => false });");

            return await context.NewPageAsync();
        }
        catch (Exception ex)
        {
            // Browser process may have crashed — reset so the next call gets a fresh browser
            logger.LogWarning(ex, "Browser page creation failed, resetting browser state for recovery");
            await ResetAsync();
            throw;
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;
            ObjectDisposedException.ThrowIf(_disposed, this);

            // SSRF guard: every connection the browser opens goes through the pinning proxy.
            _proxy = _startProxy();
            _playwright = await Playwright.CreateAsync();
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
            _initialized = true;

            logger.LogInformation("Playwright browser initialized");
        }
        catch
        {
            // A failed launch must not leave a listener behind; the next call starts a fresh one.
            _playwright?.Dispose();
            _playwright = null;
            if (_proxy != null)
            {
                await _proxy.DisposeAsync();
                _proxy = null;
            }
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Resets browser state so the next call to NewPageAsync reinitializes from scratch.
    /// Safe to call concurrently — protected by the init lock.
    /// </summary>
    private async Task ResetAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            _initialized = false;

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
        finally
        {
            _initLock.Release();
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
        _initialized = false;

        _initLock.Dispose();

        logger.LogInformation("Playwright browser disposed");
    }
}
