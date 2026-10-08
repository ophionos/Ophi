using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Push;
using Wolverine.Attributes;

namespace Ophi.Worker.Handlers;

/// <summary>
/// Channel handler for the Telegram leg of an alert fire. Rethrows on failure so Wolverine retries
/// this leg alone — see <see cref="SendAlertDiscordHandler"/>.
/// </summary>
[WolverineHandler]
[LocalQueue("notifications")]
public static class SendAlertTelegramHandler
{
    public static async Task HandleAsync(
        SendAlertTelegramRequested @event,
        ITelegramService telegram,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await telegram.SendPriceAlertAsync(new PushPriceAlert(
            @event.ProductName, @event.ProductUrl, @event.CurrentPrice, @event.TargetPrice,
            @event.Currency, @event.Condition), @event.ChatId, cancellationToken);

        logger.LogInformation("Alert Telegram notification handled for alert {AlertId}", @event.AlertId);
    }
}

/// <summary>Pushover counterpart of <see cref="SendAlertTelegramHandler"/>.</summary>
[WolverineHandler]
[LocalQueue("notifications")]
public static class SendAlertPushoverHandler
{
    public static async Task HandleAsync(
        SendAlertPushoverRequested @event,
        IPushoverService pushover,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await pushover.SendPriceAlertAsync(new PushPriceAlert(
            @event.ProductName, @event.ProductUrl, @event.CurrentPrice, @event.TargetPrice,
            @event.Currency, @event.Condition), @event.UserKey, cancellationToken);

        logger.LogInformation("Alert Pushover notification handled for alert {AlertId}", @event.AlertId);
    }
}

/// <summary>ntfy counterpart of <see cref="SendAlertTelegramHandler"/>.</summary>
[WolverineHandler]
[LocalQueue("notifications")]
public static class SendAlertNtfyHandler
{
    public static async Task HandleAsync(
        SendAlertNtfyRequested @event,
        INtfyService ntfy,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await ntfy.SendPriceAlertAsync(new PushPriceAlert(
            @event.ProductName, @event.ProductUrl, @event.CurrentPrice, @event.TargetPrice,
            @event.Currency, @event.Condition), @event.TopicUrl, cancellationToken);

        logger.LogInformation("Alert ntfy notification handled for alert {AlertId}", @event.AlertId);
    }
}
