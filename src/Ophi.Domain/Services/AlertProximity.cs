using System.Linq.Expressions;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;

namespace Ophi.Domain.Services;

/// <summary>
/// How close a product's price is to its alert targets. Only active below/above alerts in the
/// product's currency take part: a percent-drop alert has no standing target, and a target in
/// another currency is not comparable to the price (<see cref="Alert.HasCurrencyMismatch"/>).
/// </summary>
public static class AlertProximity
{
    /// <summary>A target counts as near when the price is within this fraction of it.</summary>
    public const decimal NearFraction = 0.05m;

    /// <summary>
    /// The target with the smallest gap relative to its size, so $10 off a $100 target beats $5 off
    /// a $20 one. <c>Gap</c> is how far the price still has to move, in the product's currency; zero
    /// or less means the target is reached. Ties break on the alert id.
    /// </summary>
    public static (Alert Alert, decimal Gap, bool IsNear)? Closest(
        IEnumerable<Alert> alerts, decimal? currentPrice, string productCurrency)
    {
        if (currentPrice is not { } price) return null;

        return alerts
            .Where(a => a.IsActive && a.Condition != AlertCondition.PercentDrop && a.TargetPrice > 0 &&
                        !a.HasCurrencyMismatch(productCurrency))
            .Select(a =>
            {
                var gap = a.Condition == AlertCondition.Below ? price - a.TargetPrice : a.TargetPrice - price;
                return (Alert: a, Gap: gap, IsNear: gap <= a.TargetPrice * NearFraction);
            })
            .OrderBy(x => x.Gap / x.Alert.TargetPrice)
            .ThenBy(x => x.Alert.Id)
            .Select(x => ((Alert, decimal, bool)?)x)
            .FirstOrDefault();
    }

    /// <summary>
    /// <see cref="Closest"/>'s IsNear as a query predicate, for the account-wide count and the
    /// "near target" filter. The currencies are compared upper-cased because Postgres compares text
    /// case-sensitively; AlertProximityTests runs both over the same cases.
    /// </summary>
    public static readonly Expression<Func<Product, bool>> NearTarget = p =>
        p.CurrentPrice != null && p.Alerts.Any(a =>
            a.IsActive && a.TargetPrice > 0 && a.Currency.ToUpper() == p.Currency.ToUpper() &&
            ((a.Condition == AlertCondition.Below && p.CurrentPrice <= a.TargetPrice * (1 + NearFraction)) ||
             (a.Condition == AlertCondition.Above && p.CurrentPrice >= a.TargetPrice * (1 - NearFraction))));
}
