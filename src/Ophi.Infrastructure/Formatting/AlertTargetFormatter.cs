using Ophi.Domain.Enums;

namespace Ophi.Infrastructure.Formatting;

/// <summary>
/// Renders <c>Alert.TargetPrice</c> for display.
///
/// The field is polymorphic and its type is carried by <see cref="AlertCondition"/>, not by the
/// value: for <see cref="AlertCondition.Below"/>/<see cref="AlertCondition.Above"/> it is an amount
/// in the alert's currency, but for <see cref="AlertCondition.PercentDrop"/> it is a percentage.
/// Channels that rendered it unconditionally as money turned a "20% drop" target into
/// "USD 20.00" — a number the user never set, and one that reads as a plausible price.
///
/// This is the one owner of that decision. Do not re-derive it inline in a template: the reason the
/// email and Discord payloads drifted from the in-app notification text is that each made the call
/// for itself.
/// </summary>
public static class AlertTargetFormatter
{
    /// <param name="condition">
    /// Null means "unknown" — a queued message that predates the field being carried. Falls back to
    /// the amount rendering, which is what those messages produced before, so a deploy doesn't
    /// change how in-flight alerts read.
    /// </param>
    public static string Describe(decimal targetPrice, AlertCondition? condition, string currency) =>
        condition == AlertCondition.PercentDrop
            ? $"{PriceFormatter.FormatPercent(targetPrice)}%"
            : $"{currency} {PriceFormatter.FormatPrice(targetPrice)}";
}
