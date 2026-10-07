using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ophi.Infrastructure.Push;

public class PushoverSettings
{
    /// <summary>The operator's Pushover application token (<c>PUSHOVER_APP_TOKEN</c>). Empty disables the channel.</summary>
    public string AppToken { get; set; } = string.Empty;
}

public interface IPushoverService
{
    bool IsConfigured { get; }
    Task SendPriceAlertAsync(PushPriceAlert alert, string userKey, CancellationToken cancellationToken = default);
}

public class PushoverService(
    HttpClient httpClient,
    IOptions<PushoverSettings> settings,
    ILogger<PushoverService> logger) : IPushoverService
{
    private readonly PushoverSettings _settings = settings.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.AppToken);

    public async Task SendPriceAlertAsync(PushPriceAlert alert, string userKey, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            logger.LogDebug("Pushover app token not configured, skipping notification");
            return;
        }

        // No html=1: Pushover renders the message as plain text.
        var fields = new Dictionary<string, string>
        {
            ["token"] = _settings.AppToken,
            ["user"] = userKey,
            ["title"] = PushMessage.Title(alert),
            ["message"] = PushMessage.Body(alert)
        };
        if (!string.IsNullOrWhiteSpace(alert.ProductUrl))
        {
            fields["url"] = alert.ProductUrl;
            fields["url_title"] = "View product";
        }

        var response = await httpClient.PostAsync(
            "https://api.pushover.net/1/messages.json", new FormUrlEncodedContent(fields), cancellationToken);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("Pushover notification sent for product {ProductName}", alert.ProductName);
    }
}
