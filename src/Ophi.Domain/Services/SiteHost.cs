namespace Ophi.Domain.Services;

/// <summary>
/// The site key shared by scraping, shipping rates and baskets: a host lowercased with any leading
/// "www." removed, so <c>WWW.Amazon.ES</c> and <c>amazon.es</c> are one site.
/// </summary>
public static class SiteHost
{
    public static string Normalize(string host)
    {
        host = host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
            host = host[4..];
        return host;
    }

    /// <summary>The normalized host of an absolute URL, or null when it is not one.</summary>
    public static string? Of(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? Normalize(uri.Host)
            : null;
}
