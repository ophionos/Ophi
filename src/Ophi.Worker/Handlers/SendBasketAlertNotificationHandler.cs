using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Email;
using Ophi.Infrastructure.Formatting;
using Ophi.Infrastructure.Metrics;
using Ophi.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace Ophi.Worker.Handlers;

/// <summary>
/// The basket form of <see cref="SendAlertNotificationHandler"/> (#151): persists the in-app
/// notification and the trigger count, then cascades one message per channel. The channel messages
/// are the product-alert ones with <c>Store</c> set, which makes each sender render the basket form
/// (basket name, store, total, target), so there is one sender per channel for both alert kinds.
/// The webhook leg is its own event, <c>basket_alert_fired</c>.
/// </summary>
[WolverineHandler]
[LocalQueue("notifications")]
public static class SendBasketAlertNotificationHandler
{
    public static async Task<OutgoingMessages> HandleAsync(
        BasketAlertTriggeredEvent @event,
        OphiDbContext dbContext,
        IOptions<EmailSettings> emailSettings,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();

        var alert = await dbContext.BasketAlerts
            .Include(a => a.Basket)
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.Id == @event.BasketAlertId, cancellationToken);
        if (alert == null)
        {
            logger.LogWarning("Basket alert {BasketAlertId} not found", @event.BasketAlertId);
            return outgoing;
        }

        var basketName = alert.Basket.Name;
        var link = $"{emailSettings.Value.AppUrl.TrimEnd('/')}/baskets/{alert.BasketId}";
        var target = AlertTargetFormatter.Describe(@event.TargetTotal, AlertCondition.Below, @event.Currency);

        dbContext.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = alert.UserId,
            BasketId = alert.BasketId,
            Title = Notification.BuildTitle("Basket alert", basketName),
            Message = $"Total at {@event.Host} is {@event.Currency} {PriceFormatter.FormatPrice(@event.Total)} (target: {target})",
            Type = NotificationType.BasketAlert
        });
        alert.Trigger(timeProvider.GetUtcNow().UtcDateTime);

        // As in SendAlertNotificationHandler: the notification and the trigger share one transaction,
        // and a concurrency conflict discards both instead of retrying into a duplicate notification.
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogInformation(ex,
                "Basket alert {BasketAlertId} was concurrently updated; skipping duplicate notification", @event.BasketAlertId);
            return outgoing;
        }

        AppMetrics.AlertFiredTotal.WithLabels("basket").Inc();

        var user = alert.User;
        if (user.EmailNotificationsEnabled)
        {
            outgoing.Add(new SendAlertEmailRequested(alert.Id, user.Email, user.Name, basketName, link,
                @event.Total, @event.TargetTotal, @event.Currency, AlertCondition.Below, @event.Host));
        }

        if (user.DiscordNotificationsEnabled && !string.IsNullOrWhiteSpace(user.DiscordWebhookUrl))
        {
            outgoing.Add(new SendAlertDiscordRequested(alert.Id, user.DiscordWebhookUrl, basketName, link,
                @event.Total, @event.TargetTotal, @event.Currency, AlertCondition.Below, @event.Host));
        }

        if (user.TelegramNotificationsEnabled && !string.IsNullOrWhiteSpace(user.TelegramChatId))
        {
            outgoing.Add(new SendAlertTelegramRequested(alert.Id, user.TelegramChatId, basketName, link,
                @event.Total, @event.TargetTotal, @event.Currency, AlertCondition.Below, @event.Host));
        }

        if (user.PushoverNotificationsEnabled && !string.IsNullOrWhiteSpace(user.PushoverUserKey))
        {
            outgoing.Add(new SendAlertPushoverRequested(alert.Id, user.PushoverUserKey, basketName, link,
                @event.Total, @event.TargetTotal, @event.Currency, AlertCondition.Below, @event.Host));
        }

        if (user.NtfyNotificationsEnabled && !string.IsNullOrWhiteSpace(user.NtfyTopicUrl))
        {
            outgoing.Add(new SendAlertNtfyRequested(alert.Id, user.NtfyTopicUrl, basketName, link,
                @event.Total, @event.TargetTotal, @event.Currency, AlertCondition.Below, @event.Host));
        }

        outgoing.Add(new SendBasketAlertWebhookRequested(alert.Id, alert.UserId, alert.BasketId, basketName,
            @event.Host, @event.Total, @event.TargetTotal, @event.Currency, link));

        // Refetch the bell count on open streams, as for product alerts.
        outgoing.Add(new LiveUpdate(alert.UserId, LiveUpdate.Notification));

        return outgoing;
    }
}
