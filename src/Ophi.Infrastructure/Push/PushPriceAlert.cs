using Ophi.Domain.Enums;
using Ophi.Infrastructure.Formatting;

namespace Ophi.Infrastructure.Push;

/// <summary>An alert fire as the push channels (Telegram, Pushover) render it. A basket alert sets
/// <paramref name="Store"/>: ProductName is then the basket's name and the price its total there.</summary>
public record PushPriceAlert(
    string ProductName,
    string ProductUrl,
    decimal CurrentPrice,
    decimal TargetPrice,
    string Currency,
    AlertCondition? Condition = null,
    string? Store = null);

/// <summary>
/// Plain-text rendering shared by the push channels. Deliberately no markup: neither channel is
/// asked to parse any, so a product name cannot inject formatting or links.
/// </summary>
public static class PushMessage
{
    public static string Title(PushPriceAlert alert) =>
        alert.Store is null ? $"Price alert: {alert.ProductName}" : $"Basket alert: {alert.ProductName}";

    public static string Body(PushPriceAlert alert) =>
        (alert.Store is null ? "Now" : $"Total at {alert.Store} now") +
        $" {alert.Currency} {PriceFormatter.FormatPrice(alert.CurrentPrice)} — " +
        $"target {AlertTargetFormatter.Describe(alert.TargetPrice, alert.Condition, alert.Currency)}";
}
