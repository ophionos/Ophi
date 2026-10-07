using System.Web;

namespace Ophi.Api.Common.Helpers;

public static class AffiliateUrlHelper
{
    /// <summary>
    /// Appends or replaces an affiliate query parameter on a URL.
    /// Returns the original URL unchanged if either parameter is null/empty or the URL is malformed.
    /// </summary>
    public static string ApplyAffiliateCode(string url, string? affiliateParamName, string? affiliateTag)
    {
        if (string.IsNullOrEmpty(affiliateParamName) || string.IsNullOrEmpty(affiliateTag))
            return url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var query = HttpUtility.ParseQueryString(uri.Query);

        // Remove existing param (case-insensitive) to avoid duplicates
        var existingKey = query.AllKeys.FirstOrDefault(k =>
            string.Equals(k, affiliateParamName, StringComparison.OrdinalIgnoreCase));
        if (existingKey != null)
            query.Remove(existingKey);

        query[affiliateParamName] = affiliateTag;

        var builder = new UriBuilder(uri) { Query = query.ToString() };
        return builder.Uri.ToString();
    }
}
