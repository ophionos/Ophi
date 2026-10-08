using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ophi.Infrastructure.Formatting;

namespace Ophi.Infrastructure.Discord;

public class DiscordWebhookService(
    HttpClient httpClient,
    TimeProvider timeProvider,
    ILogger<DiscordWebhookService> logger) : IDiscordService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public Task SendPriceAlertAsync(DiscordPriceAlert alert, string webhookUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            logger.LogDebug("No webhook URL provided, skipping Discord notification");
            return Task.CompletedTask;
        }

        return PostAlertAsync(alert, webhookUrl, cancellationToken);
    }

    private async Task PostAlertAsync(DiscordPriceAlert alert, string webhookUrl, CancellationToken cancellationToken)
    {
        var payload = new
        {
            embeds = new[]
            {
                new
                {
                    title = $"Price Alert: {alert.ProductName}",
                    url = string.IsNullOrWhiteSpace(alert.ProductUrl) ? null : alert.ProductUrl,
                    color = 3066993, // Green
                    fields = new[]
                    {
                        new { name = "Current Price", value = $"{alert.Currency} {PriceFormatter.FormatPrice(alert.CurrentPrice)}", inline = true },
                        new { name = "Target", value = AlertTargetFormatter.Describe(alert.TargetPrice, alert.Condition, alert.Currency), inline = true }
                    },
                    footer = new { text = "Ophi Price Tracker" },
                    timestamp = timeProvider.GetUtcNow().UtcDateTime.ToString("o")
                }
            }
        };

        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(webhookUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("Discord notification sent for product {ProductName}", alert.ProductName);
    }
}
