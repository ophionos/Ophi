using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Extensions;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Formatting;
using Ophi.Infrastructure.Metrics;
using Ophi.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace Ophi.Worker.Handlers;

/// <summary>
/// Orchestrates alert-fired delivery. Persists the in-app notification and updates the
/// alert cooldown state synchronously, then cascades one event per outbound channel so
/// each (email / Discord / Telegram / Pushover / ntfy / outbound webhook) gets independent Wolverine retry
/// semantics.
/// Email no longer carries critical-path "throw to propagate" semantics — its failure
/// retries inside <see cref="SendAlertEmailHandler"/> rather than rolling back the
/// notification persisted here.
/// </summary>
[WolverineHandler]
[LocalQueue("notifications")]
public static class SendAlertNotificationHandler
{
    public static async Task<OutgoingMessages> HandleAsync(
        AlertTriggeredEvent @event,
        OphiDbContext dbContext,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Sending notification for alert {AlertId}", @event.AlertId);

        var outgoing = new OutgoingMessages();

        var alert = await dbContext.Alerts
            .Include(a => a.Product)
                .ThenInclude(p => p.ProductUrls)
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.Id == @event.AlertId, cancellationToken);

        if (alert == null)
        {
            logger.LogWarning("Alert {AlertId} not found", @event.AlertId);
            return outgoing;
        }

        var productUrl = alert.Product.GetPrimaryUrl()?.Url ?? "";

        // The sentence structure differs per condition, but the target itself goes through the same
        // formatter the email and Discord templates use — that is the piece that drifted before.
        var target = AlertTargetFormatter.Describe(@event.TargetPrice, alert.Condition, @event.Currency);

        var message = alert.Condition switch
        {
            AlertCondition.Below =>
                $"Price dropped to {@event.Currency} {PriceFormatter.FormatPrice(@event.CurrentPrice)} (target: {target})",
            AlertCondition.Above =>
                $"Price rose to {@event.Currency} {PriceFormatter.FormatPrice(@event.CurrentPrice)} (target: {target})",
            AlertCondition.PercentDrop =>
                $"Price dropped by at least {target} to {@event.Currency} {PriceFormatter.FormatPrice(@event.CurrentPrice)}",
            _ =>
                $"Price alert triggered: {@event.Currency} {PriceFormatter.FormatPrice(@event.CurrentPrice)}"
        };

        // Persist cooldown state and in-app notification before cascading external sends.
        // Once this commits, the alert won't re-fire on the next price check; the cascaded
        // events retry independently if a downstream channel is unavailable.
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = alert.UserId,
            ProductId = alert.ProductId,
            Title = Notification.BuildTitle("Price alert", alert.Product.Name),
            Message = message,
            Type = NotificationType.PriceAlert
        };
        dbContext.Notifications.Add(notification);

        alert.Trigger(timeProvider.GetUtcNow().UtcDateTime);

        // Defense-in-depth: CheckAlertsHandler's xmin claim already prevents two AlertTriggeredEvents
        // for one firing, so a conflict here is not expected. But if a genuinely concurrent write to
        // this alert row lands, the notification insert and the alert.Trigger() update share one
        // transaction — a conflict rolls back both, so we discard quietly rather than rethrow (a retry
        // would otherwise persist a duplicate notification, since this handler has no idempotency guard
        // of its own; identical-envelope redelivery is deduped by Wolverine's durable inbox, not here).
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogInformation(ex,
                "Alert {AlertId} was concurrently updated; skipping duplicate notification", @event.AlertId);
            return outgoing;
        }

        AppMetrics.AlertFiredTotal.WithLabels(alert.Condition.ToString().ToLowerInvariant()).Inc();

        // Cascade — one event per channel. Wolverine routes these back through the bus,
        // so each handler runs in its own transaction and retries on its own failure path.
        // Email is opt-out per account, and only for alerts: password-reset mail and the SMTP test
        // send bypass this. Opting out silences the channel, not the alert — the in-app
        // notification, the webhook cascade and the live-update ping below are unaffected.
        if (alert.User.EmailNotificationsEnabled)
        {
            outgoing.Add(new SendAlertEmailRequested(
                AlertId: alert.Id,
                RecipientEmail: alert.User.Email,
                RecipientName: alert.User.Name,
                ProductName: alert.Product.Name,
                ProductUrl: productUrl,
                CurrentPrice: @event.CurrentPrice,
                TargetPrice: @event.TargetPrice,
                Currency: @event.Currency,
                // Without this the channel templates render TargetPrice as money unconditionally, so a
                // PercentDrop target of 20 goes out as "USD 20.00" instead of "20%".
                Condition: alert.Condition));
        }

        if (alert.User.DiscordNotificationsEnabled && !string.IsNullOrWhiteSpace(alert.User.DiscordWebhookUrl))
        {
            outgoing.Add(new SendAlertDiscordRequested(
                AlertId: alert.Id,
                DiscordWebhookUrl: alert.User.DiscordWebhookUrl,
                ProductName: alert.Product.Name,
                ProductUrl: productUrl,
                CurrentPrice: @event.CurrentPrice,
                TargetPrice: @event.TargetPrice,
                Currency: @event.Currency,
                Condition: alert.Condition));
        }

        if (alert.User.TelegramNotificationsEnabled && !string.IsNullOrWhiteSpace(alert.User.TelegramChatId))
        {
            outgoing.Add(new SendAlertTelegramRequested(
                AlertId: alert.Id,
                ChatId: alert.User.TelegramChatId,
                ProductName: alert.Product.Name,
                ProductUrl: productUrl,
                CurrentPrice: @event.CurrentPrice,
                TargetPrice: @event.TargetPrice,
                Currency: @event.Currency,
                Condition: alert.Condition));
        }

        if (alert.User.PushoverNotificationsEnabled && !string.IsNullOrWhiteSpace(alert.User.PushoverUserKey))
        {
            outgoing.Add(new SendAlertPushoverRequested(
                AlertId: alert.Id,
                UserKey: alert.User.PushoverUserKey,
                ProductName: alert.Product.Name,
                ProductUrl: productUrl,
                CurrentPrice: @event.CurrentPrice,
                TargetPrice: @event.TargetPrice,
                Currency: @event.Currency,
                Condition: alert.Condition));
        }

        if (alert.User.NtfyNotificationsEnabled && !string.IsNullOrWhiteSpace(alert.User.NtfyTopicUrl))
        {
            outgoing.Add(new SendAlertNtfyRequested(
                AlertId: alert.Id,
                TopicUrl: alert.User.NtfyTopicUrl,
                ProductName: alert.Product.Name,
                ProductUrl: productUrl,
                CurrentPrice: @event.CurrentPrice,
                TargetPrice: @event.TargetPrice,
                Currency: @event.Currency,
                Condition: alert.Condition));
        }

        outgoing.Add(new SendAlertWebhookRequested(
            AlertId: alert.Id,
            UserId: alert.UserId,
            ProductId: alert.ProductId,
            ProductName: alert.Product.Name,
            ProductUrl: productUrl,
            OldPrice: alert.Product.PreviousPrice,
            CurrentPrice: @event.CurrentPrice,
            Currency: @event.Currency));

        // Ping the user's open streams so the notification bell refetches its count. This fires from
        // here (not the earlier scrape-completed ping) because the in-app notification is persisted in
        // this handler, which runs after the scrape that started the cascade has already pinged.
        // Cascaded (no DeliverWithin expiry, unlike the scrape pings): a replayed notification refetch
        // after a restart is idempotent, so the stale-drop guard isn't needed here.
        outgoing.Add(new LiveUpdate(alert.UserId, LiveUpdate.Notification, alert.ProductId));

        return outgoing;
    }
}
