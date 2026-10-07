using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ophi.Infrastructure.Push;

public class TelegramSettings
{
    /// <summary>The operator's bot token (<c>TELEGRAM_BOT_TOKEN</c>). Empty disables the channel.</summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>Optional bot username, shown so users can open the bot and get their chat id.</summary>
    public string BotUsername { get; set; } = string.Empty;
}

public interface ITelegramService
{
    bool IsConfigured { get; }
    string? BotUsername { get; }
    Task SendPriceAlertAsync(PushPriceAlert alert, string chatId, CancellationToken cancellationToken = default);
}

public class TelegramService(
    HttpClient httpClient,
    IOptions<TelegramSettings> settings,
    ILogger<TelegramService> logger) : ITelegramService
{
    private readonly TelegramSettings _settings = settings.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.BotToken);

    public string? BotUsername =>
        string.IsNullOrWhiteSpace(_settings.BotUsername) ? null : _settings.BotUsername.TrimStart('@');

    public async Task SendPriceAlertAsync(PushPriceAlert alert, string chatId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            logger.LogDebug("Telegram bot token not configured, skipping notification");
            return;
        }

        // No parse_mode: plain text, so nothing in the product name is interpreted.
        var payload = new
        {
            chat_id = chatId,
            text = $"{PushMessage.Title(alert)}\n{PushMessage.Body(alert)}" +
                   (string.IsNullOrWhiteSpace(alert.ProductUrl) ? "" : $"\n{alert.ProductUrl}"),
            link_preview_options = new { is_disabled = true }
        };

        var response = await httpClient.PostAsJsonAsync(
            $"https://api.telegram.org/bot{_settings.BotToken}/sendMessage", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("Telegram notification sent for product {ProductName}", alert.ProductName);
    }
}
