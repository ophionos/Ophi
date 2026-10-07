using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Discord;
using Wolverine.Attributes;

namespace Ophi.Worker.Handlers;

/// <summary>
/// Channel handler for the Discord leg of an alert fire. The orchestrator only cascades
/// this event when the user has Discord enabled and a webhook URL configured, so the
/// handler can send unconditionally.
/// </summary>
[WolverineHandler]
[LocalQueue("notifications")]
public static class SendAlertDiscordHandler
{
    public static async Task HandleAsync(
        SendAlertDiscordRequested @event,
        IDiscordService discordService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await discordService.SendPriceAlertAsync(new DiscordPriceAlert(
            @event.ProductName,
            @event.ProductUrl,
            @event.CurrentPrice,
            @event.TargetPrice,
            @event.Currency,
            @event.Condition
        ), @event.DiscordWebhookUrl, cancellationToken);

        logger.LogInformation("Alert Discord notification sent for product {ProductName} (alert {AlertId})",
            @event.ProductName, @event.AlertId);
    }
}
