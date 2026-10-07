namespace Ophi.Domain.Messages.Events;

/// <summary>
/// Fire-and-forget signal that something a user is looking at changed, so the browser should refetch.
/// Deliberately decoupled from <see cref="PriceUpdatedEvent"/>: that one is alert-critical, processed
/// serially on the <c>events</c> queue with the xmin race machinery, and only fires on a price change.
/// This one is a "thin ping" for the SSE transport — it carries no price data and fires on every
/// scrape outcome plus on notification creation, so it can never perturb the alert cascade.
///
/// <para>
/// <see cref="UserId"/> is carried so the API can fan out to that user's open streams without a DB
/// lookup. <see cref="Kind"/> tells the browser which surfaces to refresh.
/// </para>
/// </summary>
/// <param name="UserId">Owner of the affected data; used for per-user SSE fan-out.</param>
/// <param name="Kind">
/// <see cref="ScrapeCompleted"/> (a scrape finished — refresh product/price surfaces) or
/// <see cref="Notification"/> (an in-app notification was created — refresh the bell count).
/// </param>
/// <param name="ProductId">The affected product, when applicable, so a detail page can ignore others.</param>
public record LiveUpdate(Guid UserId, string Kind, Guid? ProductId = null)
{
    public const string ScrapeCompleted = "scrape-completed";
    public const string Notification = "notification";
}
