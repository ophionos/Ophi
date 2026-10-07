using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Email;
using Wolverine.Attributes;

namespace Ophi.Worker.Handlers;

/// <summary>
/// Channel handler for the email leg of an alert fire. Runs on the notifications queue
/// so it can be retried independently of Discord and outbound-webhook delivery — a
/// transient SMTP outage no longer takes down the rest of the alert pipeline.
/// </summary>
[WolverineHandler]
[LocalQueue("notifications")]
public static class SendAlertEmailHandler
{
    public static async Task HandleAsync(
        SendAlertEmailRequested @event,
        IEmailService emailService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await emailService.SendPriceAlertAsync(new PriceAlertEmail(
            @event.RecipientEmail,
            @event.RecipientName,
            @event.ProductName,
            @event.ProductUrl,
            @event.CurrentPrice,
            @event.TargetPrice,
            @event.Currency,
            @event.Condition
        ), cancellationToken);

        logger.LogInformation("Alert email sent to {Email} for product {ProductName} (alert {AlertId})",
            @event.RecipientEmail, @event.ProductName, @event.AlertId);
    }
}
