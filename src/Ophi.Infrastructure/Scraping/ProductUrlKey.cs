namespace Ophi.Infrastructure.Scraping;

/// <summary>
/// The comparison key for "is this the same product URL?" — used by every duplicate check and by the
/// URL lookup. It is never stored and never fetched: the stored URL stays exactly as the user gave
/// it, because scraping and the redirect check in <c>ScrapeHealthAnalyzer</c> work from that string.
/// Keeping the key out of the database also means existing rows match without a backfill.
///
/// Only parameters known to be tracking noise are dropped. Many stores select the variant or SKU
/// through a query parameter (<c>variant</c>, Amazon <c>th</c>/<c>psc</c>/<c>smid</c>), and dropping
/// one silently tracks a different price. Amazon-only keys are dropped only on Amazon hosts, where
/// their meaning is known.
/// </summary>
public static class ProductUrlKey
{
    private static readonly string[] TrackingPrefixes = ["utm_"];

    private static readonly HashSet<string> TrackingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "gclsrc", "dclid", "gbraid", "wbraid", "msclkid", "yclid", "twclid",
        "ttclid", "igshid", "mc_cid", "mc_eid", "_ga", "_gl", "srsltid"
    };

    private static readonly string[] AmazonTrackingPrefixes = ["pd_rd_", "pf_rd_"];

    private static readonly HashSet<string> AmazonTrackingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "qid", "sr", "keywords", "crid", "sprefix", "ref", "ref_", "content-id", "dib", "dib_tag",
        "_encoding", "tag", "ascsubtag", "linkCode", "linkId", "social_share", "starsLeft", "spLa", "sp_csd"
    };

    public static string For(string url)
    {
        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return trimmed;
        }

        var host = ScrapeHelpers.NormalizeHost(uri.Host);
        var isAmazon = IsAmazonHost(host);

        // http and https are the same product: the store redirects one to the other.
        var authority = uri.IsDefaultPort ? host : $"{host}:{uri.Port}";

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (isAmazon)
            segments = segments.Where(s => !s.StartsWith("ref=", StringComparison.OrdinalIgnoreCase)).ToArray();
        var path = "/" + string.Join('/', segments);

        var parameters = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !IsTracking(Uri.UnescapeDataString(pair.Split('=', 2)[0]), isAmazon))
            .Order(StringComparer.Ordinal)
            .ToList();

        return parameters.Count == 0
            ? $"{authority}{path}"
            : $"{authority}{path}?{string.Join('&', parameters)}";
    }

    private static bool IsTracking(string key, bool isAmazon) =>
        TrackingKeys.Contains(key)
        || TrackingPrefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase))
        || (isAmazon && (AmazonTrackingKeys.Contains(key)
            || AmazonTrackingPrefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase))));

    // amazon.com, amazon.co.uk, smile.amazon.de, ...
    private static bool IsAmazonHost(string host) =>
        host.StartsWith("amazon.", StringComparison.Ordinal) || host.Contains(".amazon.", StringComparison.Ordinal);
}
