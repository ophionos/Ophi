using FluentAssertions;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Services;

namespace Ophi.Domain.Tests;

public class AlertProximityTests
{
    private static Alert Target(decimal target, AlertCondition condition = AlertCondition.Below, string currency = "USD") =>
        new() { Id = Guid.NewGuid(), TargetPrice = target, Condition = condition, Currency = currency, IsActive = true };

    [Fact]
    public void Closest_PicksTheTargetWithTheSmallestRelativeGap()
    {
        // $10 above a $100 target (10%) is closer than $5 above a $20 target (25%).
        var far = Target(20m);
        var near = Target(100m);

        var result = AlertProximity.Closest([far, near], 110m, "USD");

        result!.Value.Alert.Should().BeSameAs(near);
        result.Value.Gap.Should().Be(10m);
    }

    [Fact]
    public void Closest_MeasuresAnAboveAlertFromBelow()
    {
        var result = AlertProximity.Closest([Target(100m, AlertCondition.Above)], 90m, "USD");

        result!.Value.Gap.Should().Be(10m);
    }

    [Fact]
    public void Closest_ReportsAReachedTargetAsANonPositiveGap()
    {
        var result = AlertProximity.Closest([Target(100m)], 95m, "USD");

        result!.Value.Gap.Should().Be(-5m);
        result.Value.IsNear.Should().BeTrue();
    }

    [Fact]
    public void Closest_SkipsPausedPercentDropAndOtherCurrencyAlerts()
    {
        var paused = Target(100m);
        paused.Pause();
        var percent = Target(10m, AlertCondition.PercentDrop);
        var euro = Target(100m, currency: "EUR");

        AlertProximity.Closest([paused, percent, euro], 101m, "USD").Should().BeNull();
    }

    [Fact]
    public void Closest_MatchesTheCurrencyWithoutRegardToCase()
    {
        AlertProximity.Closest([Target(100m, currency: "usd")], 101m, "USD").Should().NotBeNull();
    }

    [Fact]
    public void Closest_WithoutACurrentPrice_ReturnsNull()
    {
        AlertProximity.Closest([Target(100m)], null, "USD").Should().BeNull();
    }

    [Theory]
    [InlineData(105, AlertCondition.Below, true)]
    [InlineData(105.01, AlertCondition.Below, false)]
    [InlineData(95, AlertCondition.Above, true)]
    [InlineData(94.99, AlertCondition.Above, false)]
    public void IsNear_MeansWithinFivePercentOfTheTarget(double price, AlertCondition condition, bool expected)
    {
        var result = AlertProximity.Closest([Target(100m, condition)], (decimal)price, "USD");

        result!.Value.IsNear.Should().Be(expected);
    }

    [Theory]
    [InlineData(104, AlertCondition.Below, "USD", true, true)]
    [InlineData(110, AlertCondition.Below, "USD", true, false)]
    [InlineData(96, AlertCondition.Above, "USD", true, true)]
    [InlineData(104, AlertCondition.Below, "usd", true, true)]
    [InlineData(104, AlertCondition.Below, "EUR", true, false)]
    [InlineData(104, AlertCondition.Below, "USD", false, false)]
    [InlineData(1, AlertCondition.PercentDrop, "USD", true, false)]
    public void NearTarget_AgreesWithClosest(double price, AlertCondition condition, string currency, bool active, bool expected)
    {
        // The SQL predicate restates Closest(...).IsNear; this keeps the two from drifting apart.
        var alert = Target(100m, condition, currency);
        if (!active) alert.Pause();
        var product = new Product { CurrentPrice = (decimal)price, Currency = "USD", Alerts = [alert] };

        var viaPredicate = AlertProximity.NearTarget.Compile()(product);
        var viaClosest = AlertProximity.Closest(product.Alerts, product.CurrentPrice, product.Currency)?.IsNear ?? false;

        viaPredicate.Should().Be(expected);
        viaClosest.Should().Be(expected);
    }
}
