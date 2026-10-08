using FluentAssertions;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;

namespace Ophi.Infrastructure.Tests.Domain;

public class AlertTests
{
    [Fact]
    public void Trigger_SetsLastTriggeredAtAndIncrementsTriggerCount()
    {
        var alert = new Alert { TargetPrice = 50m, Condition = AlertCondition.Below };
        var now = new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc);

        alert.Trigger(now);

        alert.LastTriggeredAt.Should().Be(now);
        alert.TriggerCount.Should().Be(1);
    }

    [Fact]
    public void Trigger_OnAlreadyTriggeredAlert_KeepsCountMonotonic()
    {
        var alert = new Alert { TargetPrice = 50m, TriggerCount = 5, LastTriggeredAt = DateTime.UtcNow.AddDays(-1) };
        var now = DateTime.UtcNow;

        alert.Trigger(now);

        alert.TriggerCount.Should().Be(6);
        alert.LastTriggeredAt.Should().Be(now);
    }

    [Fact]
    public void ClaimFiring_StampsLastTriggeredAtWithoutCounting()
    {
        // The checker's claim (xmin race) and the delivered count are two steps: the count moves only
        // once SendAlertNotificationHandler commits the notification, via Trigger().
        var alert = new Alert { TargetPrice = 50m, TriggerCount = 2 };
        var now = new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc);

        alert.ClaimFiring(now);

        alert.LastTriggeredAt.Should().Be(now);
        alert.TriggerCount.Should().Be(2);
    }

    [Fact]
    public void IsInCooldown_WithNoPriorTrigger_ReturnsFalse()
    {
        var alert = new Alert { TargetPrice = 50m };

        alert.IsInCooldown(TimeSpan.FromHours(1), DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void IsInCooldown_WithinWindow_ReturnsTrue()
    {
        var now = new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc);
        var alert = new Alert { TargetPrice = 50m, LastTriggeredAt = now.AddMinutes(-30) };

        alert.IsInCooldown(TimeSpan.FromHours(1), now).Should().BeTrue();
    }

    [Fact]
    public void IsInCooldown_BeyondWindow_ReturnsFalse()
    {
        var now = new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc);
        var alert = new Alert { TargetPrice = 50m, LastTriggeredAt = now.AddHours(-2) };

        alert.IsInCooldown(TimeSpan.FromHours(1), now).Should().BeFalse();
    }

    [Fact]
    public void IsInCooldown_AtExactWindow_ReturnsFalse()
    {
        // The cooldown comparison is strict-less-than: at the boundary, the alert is past cooldown.
        var now = new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc);
        var alert = new Alert { TargetPrice = 50m, LastTriggeredAt = now.AddHours(-1) };

        alert.IsInCooldown(TimeSpan.FromHours(1), now).Should().BeFalse();
    }

    [Theory]
    [InlineData(45, true)]    // at-or-below target → fires
    [InlineData(50, true)]    // exact target → fires
    [InlineData(51, false)]   // above target → no fire
    public void ShouldTrigger_BelowCondition(decimal currentPrice, bool expected)
    {
        var alert = new Alert { TargetPrice = 50m, Currency = "USD", Condition = AlertCondition.Below };

        alert.ShouldTrigger(currentPrice, "USD").Should().Be(expected);
    }

    [Theory]
    [InlineData(75, false)]   // below target → no fire
    [InlineData(80, true)]    // exact target → fires
    [InlineData(90, true)]    // above target → fires
    public void ShouldTrigger_AboveCondition(decimal currentPrice, bool expected)
    {
        var alert = new Alert { TargetPrice = 80m, Currency = "USD", Condition = AlertCondition.Above };

        alert.ShouldTrigger(currentPrice, "USD").Should().Be(expected);
    }

    [Theory]
    [InlineData(100, 80, true)]    // 20% drop hits the 20% target
    [InlineData(100, 70, true)]    // 30% drop exceeds the 20% target
    [InlineData(100, 90, false)]   // 10% drop doesn't reach the 20% target
    public void ShouldTrigger_PercentDropCondition(decimal oldPrice, decimal currentPrice, bool expected)
    {
        var alert = new Alert { TargetPrice = 20m, Currency = "USD", Condition = AlertCondition.PercentDrop };

        alert.ShouldTrigger(currentPrice, "USD", oldPrice, "USD").Should().Be(expected);
    }

    [Fact]
    public void ShouldTrigger_PercentDropWithoutOldPrice_DoesNotFire()
    {
        // Percent-drop needs a reference price; missing or zero refs return false (avoid divide-by-zero
        // and avoid firing on the very first scrape when no history exists yet).
        var alert = new Alert { TargetPrice = 20m, Currency = "USD", Condition = AlertCondition.PercentDrop };

        alert.ShouldTrigger(80m, "USD", oldPrice: null).Should().BeFalse();
        alert.ShouldTrigger(80m, "USD", oldPrice: 0m).Should().BeFalse();
    }

    [Fact]
    public void ShouldTrigger_InactiveAlert_NeverFires()
    {
        var alert = new Alert { TargetPrice = 50m, Currency = "USD", Condition = AlertCondition.Below, IsActive = false };

        alert.ShouldTrigger(40m, "USD").Should().BeFalse();
    }

    // ---- Currency guard -------------------------------------------------------------------
    // A price is a number *and* a denomination. Comparing the raw decimals across currencies is
    // meaningless, so an alert whose currency no longer matches the product's goes dormant rather
    // than firing on a number that happens to clear the threshold.

    [Theory]
    [InlineData(AlertCondition.Below, 75)]   // EUR 75 numerically clears a USD 80 "below" target
    [InlineData(AlertCondition.Above, 95)]   // EUR 95 numerically clears a USD 80 "above" target
    public void ShouldTrigger_WhenProductCurrencyDiffersFromAlert_DoesNotFire(
        AlertCondition condition, decimal currentPrice)
    {
        // The non-US-egress repro: a USD product re-anchors to EUR after a scrape, and the
        // re-denominated number clears the threshold that was set in USD.
        var alert = new Alert { TargetPrice = 80m, Currency = "USD", Condition = condition };

        alert.ShouldTrigger(currentPrice, "EUR").Should().BeFalse(
            "the target was set in USD and cannot be compared against a EUR price");
    }

    [Fact]
    public void ShouldTrigger_CurrencyComparisonIsCaseInsensitive()
    {
        // Scrapers and store overrides are not consistent about casing; "usd" and "USD" are the
        // same denomination and must not read as a mismatch.
        var alert = new Alert { TargetPrice = 80m, Currency = "USD", Condition = AlertCondition.Below };

        alert.ShouldTrigger(75m, "usd").Should().BeTrue();
    }

    [Fact]
    public void ShouldTrigger_PercentDropAcrossCurrencyChange_DoesNotFire()
    {
        // Distinct from the guard above: here the alert and the product agree on EUR, so the
        // headline check passes — but the *drop* is computed from a USD 100 old price against a
        // EUR 75 new one. (100-75)/100 is arithmetic on two denominations and means nothing.
        var alert = new Alert { TargetPrice = 20m, Currency = "EUR", Condition = AlertCondition.PercentDrop };

        alert.ShouldTrigger(75m, "EUR", oldPrice: 100m, oldCurrency: "USD").Should().BeFalse();
    }

    [Fact]
    public void ShouldTrigger_PercentDropWithUnknownOldCurrency_AssumesUnchanged()
    {
        // A null oldCurrency means the event predates the field — only in-flight durable messages
        // during a deploy. The headline alert-vs-product guard already blocks the persistent-flip
        // case, so assume the denomination held rather than silently suppressing a real drop.
        var alert = new Alert { TargetPrice = 20m, Currency = "USD", Condition = AlertCondition.PercentDrop };

        alert.ShouldTrigger(80m, "USD", oldPrice: 100m, oldCurrency: null).Should().BeTrue();
    }

    [Fact]
    public void HasCurrencyMismatch_ReflectsWhetherTheAlertCanStillBeEvaluated()
    {
        var alert = new Alert { TargetPrice = 80m, Currency = "USD", Condition = AlertCondition.Below };

        alert.HasCurrencyMismatch("EUR").Should().BeTrue();
        alert.HasCurrencyMismatch("usd").Should().BeFalse();
    }

    [Fact]
    public void HasCurrencyMismatch_ForPercentDrop_IsAlwaysFalse()
    {
        // A percent-drop target is a percentage, not an amount — it stays meaningful whatever the
        // product is denominated in, so such an alert never goes dormant on a re-anchor.
        var alert = new Alert { TargetPrice = 20m, Currency = "USD", Condition = AlertCondition.PercentDrop };

        alert.HasCurrencyMismatch("EUR").Should().BeFalse();
    }

    [Fact]
    public void Redenominate_AdoptsTheNewCurrencyAndTarget_AndWakesTheAlert()
    {
        var alert = new Alert { TargetPrice = 80m, Currency = "USD", Condition = AlertCondition.Below };

        alert.Redenominate(75m, "EUR");

        alert.Currency.Should().Be("EUR");
        alert.TargetPrice.Should().Be(75m);
        alert.HasCurrencyMismatch("EUR").Should().BeFalse("re-denominating is what ends dormancy");
        alert.ShouldTrigger(70m, "EUR").Should().BeTrue("the alert evaluates against the new target");
    }

    [Fact]
    public void Redenominate_DoesNotCarryTheOldTargetForward()
    {
        // The whole point: 80 USD is not 80 EUR. Re-stamping the currency while keeping the number
        // would convert the alert into a target the user never chose — the silent re-denomination
        // that the dormancy rule exists to prevent.
        var alert = new Alert { TargetPrice = 80m, Currency = "USD", Condition = AlertCondition.Below };

        alert.Redenominate(69m, "EUR");

        alert.TargetPrice.Should().NotBe(80m);
        alert.TargetPrice.Should().Be(69m);
    }

    [Fact]
    public void Redenominate_LeavesTriggerHistoryIntact()
    {
        // The trigger log is a record of what happened, not state describing the current target.
        // Re-denominating changes what the alert watches for; it does not un-fire past alerts.
        var firedAt = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var alert = new Alert { TargetPrice = 80m, Currency = "USD", Condition = AlertCondition.Below };
        alert.Trigger(firedAt);

        alert.Redenominate(69m, "EUR");

        alert.LastTriggeredAt.Should().Be(firedAt);
        alert.TriggerCount.Should().Be(1);
    }

    [Fact]
    public void Redenominate_ToTheCurrencyItAlreadyUses_Throws()
    {
        // Re-denominating a live alert is just editing its target, which is a different feature.
        // Refusing here keeps this action narrow rather than a back-door UpdateAlert.
        var alert = new Alert { TargetPrice = 80m, Currency = "USD", Condition = AlertCondition.Below };

        var act = () => alert.Redenominate(75m, "usd");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Redenominate_APercentDropAlert_Throws()
    {
        // A percent-drop target is a percentage and never goes dormant, so there is nothing here to
        // recover — and re-stamping its currency would imply its target had a denomination.
        var alert = new Alert { TargetPrice = 20m, Currency = "USD", Condition = AlertCondition.PercentDrop };

        var act = () => alert.Redenominate(15m, "EUR");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Pause_OnActiveAlert_MakesItInactive()
    {
        var alert = new Alert { TargetPrice = 50m, IsActive = true };

        alert.Pause();

        alert.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Resume_OnPausedAlert_MakesItActive()
    {
        var alert = new Alert { TargetPrice = 50m, IsActive = false };

        alert.Resume();

        alert.IsActive.Should().BeTrue();
    }

    [Fact]
    public void PauseAndResume_LeaveTriggerHistoryUntouched()
    {
        // Pausing is not a reset: the history is what tells the user the alert has worked before.
        var firedAt = new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc);
        var alert = new Alert { TargetPrice = 50m, LastTriggeredAt = firedAt, TriggerCount = 3 };

        alert.Pause();
        alert.Resume();

        alert.LastTriggeredAt.Should().Be(firedAt);
        alert.TriggerCount.Should().Be(3);
    }
}
