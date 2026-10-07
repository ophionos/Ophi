namespace Ophi.Domain.Messages.Events;

/// <summary>
/// Emitted after a successful product-URL scrape. Carries pricing at two tiers:
/// <list type="bullet">
///   <item>
///     Product-level (<see cref="OldPrice"/>, <see cref="NewPrice"/>, <see cref="Currency"/>):
///     the MIN-across-URLs aggregate before and after this scrape. Consumers reacting to
///     "the product's headline price changed" (alerts, price-changed webhook) read these.
///   </item>
///   <item>
///     URL-level (<see cref="UrlPrice"/>, <see cref="UrlCurrency"/>): the value just scraped from
///     <see cref="ProductUrlId"/>. Consumers recording per-URL history read these.
///   </item>
/// </list>
/// For single-URL products the two tiers coincide, so the URL-level fields default to <c>null</c>
/// and the per-URL consumer falls back to <see cref="NewPrice"/>/<see cref="Currency"/>.
/// </summary>
/// <param name="OldCurrency">
/// Denomination of <see cref="OldPrice"/>, which is not always <see cref="Currency"/>: a scrape can
/// re-anchor the product onto a different currency, so the before and after prices are then in
/// different denominations. Percent-drop alerts need this to avoid reading a re-denomination as a
/// price movement. Declared last with a <c>null</c> default so messages already sitting in the
/// durable Wolverine queue at deploy time still deserialize; <c>null</c> means "unknown", not "same".
/// </param>
public record PriceUpdatedEvent(
    Guid ProductId,
    decimal? OldPrice,
    decimal NewPrice,
    string Currency,
    Guid? ProductUrlId = null,
    decimal? UrlPrice = null,
    string? UrlCurrency = null,
    string? OldCurrency = null);
