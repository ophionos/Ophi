using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Ophi.Infrastructure.Push;

public interface INtfyService
{
    Task SendPriceAlertAsync(PushPriceAlert alert, string topicUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publishes to the user's own ntfy topic (ntfy.sh or self-hosted). No operator setup: the topic URL is
/// the whole configuration, like a Discord webhook URL.
/// </summary>
public partial class NtfyService(HttpClient httpClient, ILogger<NtfyService> logger) : INtfyService
{
    // ntfy's own topic rule: 1–64 of letters, digits, '-' and '_'.
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex TopicPattern();

    // Path names ntfy reserves for subscribing ("/topic/json") and its own API. A URL ending in one
    // is not a topic to publish to.
    private static readonly HashSet<string> ReservedNames = ["json", "sse", "raw", "ws", "auth"];

    /// <summary>
    /// An http(s) URL whose last path segment is a valid topic, with no query or fragment. The path
    /// before it is kept as the server's base path, for an ntfy behind a reverse-proxy prefix.
    /// </summary>
    public static bool IsValidTopicUrl(string? url) => TrySplit(url, out _, out _);

    public async Task SendPriceAlertAsync(PushPriceAlert alert, string topicUrl, CancellationToken cancellationToken = default)
    {
        if (!TrySplit(topicUrl, out var serverRoot, out var topic))
            throw new ArgumentException("Not a valid ntfy topic URL.", nameof(topicUrl));

        // JSON publishing rather than the Title/Click headers: header values cannot carry most of the
        // characters a product name may hold. No "markdown": true, so the text renders as plain text.
        var response = await httpClient.PostAsJsonAsync(serverRoot, new
        {
            topic,
            title = PushMessage.Title(alert),
            message = PushMessage.Body(alert),
            click = string.IsNullOrWhiteSpace(alert.ProductUrl) ? null : alert.ProductUrl
        }, cancellationToken);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("ntfy notification sent for product {ProductName}", alert.ProductName);
    }

    private static bool TrySplit(string? url, out Uri serverRoot, out string topic)
    {
        serverRoot = null!;
        topic = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return false;

        var path = uri.AbsolutePath;
        var lastSlash = path.LastIndexOf('/');
        var candidate = path[(lastSlash + 1)..];
        if (!TopicPattern().IsMatch(candidate) || ReservedNames.Contains(candidate))
            return false;

        serverRoot = new Uri(uri, path[..(lastSlash + 1)]);
        topic = candidate;
        return true;
    }
}
