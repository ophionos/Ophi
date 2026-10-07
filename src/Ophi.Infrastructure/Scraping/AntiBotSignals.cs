namespace Ophi.Infrastructure.Scraping;

/// <summary>
/// Recognises anti-bot challenge pages, which are indistinguishable from a real page by status code
/// alone: the challenge is served as HTTP 200 with well-formed HTML. Without this, extraction simply
/// finds no price and the run is recorded as <see cref="ScrapeErrorCategory.ParseError"/> — "could
/// not extract price from page" — which blames our parser for the site refusing us.
///
/// <para>
/// Shared by both scraping paths deliberately. The Playwright service used to carry its own inline
/// pair of Cloudflare titles, so every non-Cloudflare challenge went unrecognised, and the HTTP
/// service — which is the path every store without <c>RequiresJavaScript</c> actually takes — had no
/// detection at all.
/// </para>
///
/// <para>
/// Matching is deliberately narrow: exact titles and an exact URL path. A false positive is worse
/// than a miss here. A miss degrades to the old behaviour (a confusing but harmless parse error),
/// whereas a false positive tells the user a working store is blocking us and strands the product.
/// That is why there is no substring or keyword matching on page text.
/// </para>
/// </summary>
public static class AntiBotSignals
{
    /// <summary>
    /// Page titles served by challenge interstitials, matched in full and case-insensitively.
    /// Cloudflare's two are long-standing; "Robot or human?" is PerimeterX, observed on walmart.com
    /// product requests from this deployment's egress (issue #131).
    /// </summary>
    public static readonly string[] ChallengeTitles =
    [
        "Just a moment...",
        "Attention Required! | Cloudflare",
        "Robot or human?"
    ];

    /// <summary>
    /// URL paths a blocked request is redirected to, matched as the whole path. Walmart answers at
    /// <c>/blocked?url=&lt;base64 of the original path&gt;</c>. Kept alongside the title check so a
    /// re-worded interstitial doesn't silently reopen the gap; kept as an exact path, never a
    /// substring, so a product slug containing the word can't trip it.
    /// </summary>
    public static readonly string[] ChallengePaths =
    [
        "/blocked"
    ];

    /// <summary>True when the page title is exactly one of <see cref="ChallengeTitles"/>.</summary>
    public static bool IsChallengeTitle(string? title) =>
        !string.IsNullOrWhiteSpace(title)
        && ChallengeTitles.Contains(title.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when the URL is an absolute http(s) URL whose path is exactly one of
    /// <see cref="ChallengePaths"/>. Unparseable input is not a challenge — guessing would invent
    /// blocks.
    ///
    /// <para>
    /// The scheme check is load-bearing, not defensive tidiness. On Linux — which is where this
    /// runs in production — <c>Uri.TryCreate("/blocked", UriKind.Absolute, …)</c> <b>succeeds</b>,
    /// parsing a bare POSIX path as <c>file:///blocked</c>, so a path-only string would match. The
    /// same call returns false on Windows, so a dev-machine test run hides it. Only http(s) can be
    /// a scrape target, so requiring the scheme fixes the divergence and narrows the matcher.
    /// </para>
    /// </summary>
    public static bool IsChallengeUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && ChallengePaths.Contains(uri.AbsolutePath.TrimEnd('/'), StringComparer.OrdinalIgnoreCase);

    /// <summary>True when either signal fires.</summary>
    public static bool IsChallenge(string? title, string? url) =>
        IsChallengeTitle(title) || IsChallengeUrl(url);
}
