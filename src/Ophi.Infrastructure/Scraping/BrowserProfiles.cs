namespace Ophi.Infrastructure.Scraping;

/// <summary>
/// Represents a realistic browser fingerprint for HTTP scraping requests.
/// Ensures User-Agent and all accompanying headers are internally consistent —
/// Chrome profiles include Sec-Ch-Ua/Sec-Fetch headers; Firefox/Safari do not.
/// </summary>
internal record BrowserProfile(
    string UserAgent,
    string Accept,
    string AcceptLanguage,
    string? SecChUa,
    string? SecChUaMobile,
    string? SecChUaPlatform
)
{
    /// <summary>True for Chrome and Edge profiles that send Sec-Ch-Ua headers.</summary>
    public bool IsChromium => SecChUa != null;
}

/// <summary>
/// A curated pool of realistic browser profiles for HTTP scraping.
/// Profiles are internally consistent — each set of headers matches its User-Agent.
/// </summary>
internal static class BrowserProfiles
{
    // Chrome/Edge Accept includes avif, webp, apng, and signed-exchange
    private const string ChromeAccept =
        "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7";

    // Firefox Accept includes avif and webp but not apng/signed-exchange
    private const string FirefoxAccept =
        "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8";

    // Safari Accept is simpler
    private const string SafariAccept =
        "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

    private const string ChromeAcceptLanguage = "en-US,en;q=0.9";
    private const string FirefoxAcceptLanguage = "en-US,en;q=0.5";

    private static readonly BrowserProfile[] All =
    [
        // Chrome 131 — Windows
        new(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
            ChromeAccept,
            ChromeAcceptLanguage,
            "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
            "?0",
            "\"Windows\""
        ),
        // Chrome 131 — macOS
        new(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
            ChromeAccept,
            ChromeAcceptLanguage,
            "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
            "?0",
            "\"macOS\""
        ),
        // Chrome 131 — Linux
        new(
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
            ChromeAccept,
            ChromeAcceptLanguage,
            "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
            "?0",
            "\"Linux\""
        ),
        // Edge 131 — Windows
        new(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0",
            ChromeAccept,
            ChromeAcceptLanguage,
            "\"Microsoft Edge\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
            "?0",
            "\"Windows\""
        ),
        // Firefox 133 — Windows (no Sec-Ch-Ua)
        new(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0",
            FirefoxAccept,
            FirefoxAcceptLanguage,
            null,
            null,
            null
        ),
        // Firefox 133 — macOS (no Sec-Ch-Ua)
        new(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:133.0) Gecko/20100101 Firefox/133.0",
            FirefoxAccept,
            FirefoxAcceptLanguage,
            null,
            null,
            null
        ),
        // Safari 17.2 — macOS (no Sec-Ch-Ua)
        new(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_2_1) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Safari/605.1.15",
            SafariAccept,
            "en-US,en;q=0.9",
            null,
            null,
            null
        ),
    ];

    /// <summary>Returns a randomly selected browser profile.</summary>
    public static BrowserProfile GetRandom() => All[Random.Shared.Next(All.Length)];

    /// <summary>Returns the complete list of profiles (exposed for testing).</summary>
    internal static IReadOnlyList<BrowserProfile> GetAll() => All;
}
