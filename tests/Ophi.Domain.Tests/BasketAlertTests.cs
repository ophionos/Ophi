using FluentAssertions;
using Ophi.Domain.Entities;
using Ophi.Domain.Services;

namespace Ophi.Domain.Tests;

public class BasketAlertTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private static ProductUrl Url(string host, decimal price, string currency = "EUR") =>
        new() { Id = Guid.NewGuid(), Url = $"https://www.{host}/p/{Guid.NewGuid():N}", CurrentPrice = price, Currency = currency };

    private static BasketTotals.Result Totals(params BasketTotals.Item[] items) => BasketTotals.Compute(items, _ => null);

    private static BasketAlert Alert(decimal target, string? host = null, string currency = "EUR", bool active = true) =>
        new() { Id = Guid.NewGuid(), BasketId = Guid.NewGuid(), UserId = Guid.NewGuid(), TargetTotal = target, Currency = currency, Host = host, IsActive = active };

    // one.example has both items (30); two.example has only A (8), so it is incomplete.
    private static readonly BasketTotals.Result OneCompleteOneNot = Totals(
        new BasketTotals.Item(A, 1, [Url("one.example", 10m), Url("two.example", 8m)]),
        new BasketTotals.Item(B, 1, [Url("one.example", 20m)]));

    [Fact]
    public void Check_BestStoreAtOrUnderTarget_Triggers()
    {
        var check = Alert(30m).Check(OneCompleteOneNot);

        check.Should().Be(new BasketAlert.Watched("one.example", 30m, "EUR"));
        Alert(30m).ShouldTrigger(check).Should().BeTrue();
        Alert(29.99m).ShouldTrigger(check).Should().BeFalse();
    }

    [Fact]
    public void Check_IncompleteStore_NeverTriggers()
    {
        // two.example's partial subtotal (8) is under the target, but the store does not have every item.
        var alert = Alert(10m, host: "two.example");

        alert.ShouldTrigger(alert.Check(OneCompleteOneNot)).Should().BeFalse();
        alert.Check(OneCompleteOneNot).Should().BeNull();
    }

    [Fact]
    public void Check_HostAlert_WatchesOnlyThatStore()
    {
        var totals = Totals(
            new BasketTotals.Item(A, 1, [Url("one.example", 10m), Url("two.example", 50m)]));
        var alert = Alert(20m, host: "two.example");

        alert.Check(totals).Should().Be(new BasketAlert.Watched("two.example", 50m, "EUR"));
        alert.ShouldTrigger(alert.Check(totals)).Should().BeFalse();
    }

    [Fact]
    public void Check_HostMatchesTheNormalizedSite()
    {
        var totals = Totals(new BasketTotals.Item(A, 1, [Url("one.example", 10m)]));

        Alert(20m, host: "WWW.One.Example").Check(totals)!.Value.Host.Should().Be("one.example");
    }

    [Fact]
    public void ShouldTrigger_WatchedTotalInAnotherCurrency_IsDormant()
    {
        var totals = Totals(new BasketTotals.Item(A, 1, [Url("one.example", 10m, "USD")]));
        var alert = Alert(20m, currency: "EUR");

        alert.HasCurrencyMismatch(alert.Check(totals)!.Value.Currency).Should().BeTrue();
        alert.ShouldTrigger(alert.Check(totals)).Should().BeFalse();
    }

    [Fact]
    public void ShouldTrigger_Paused_IsFalse()
    {
        var alert = Alert(30m);
        alert.Pause();

        alert.ShouldTrigger(alert.Check(OneCompleteOneNot)).Should().BeFalse();
        alert.Resume();
        alert.ShouldTrigger(alert.Check(OneCompleteOneNot)).Should().BeTrue();
    }

    [Fact]
    public void Cooldown_AfterAClaim_HoldsForTheWindow()
    {
        var alert = Alert(30m);
        alert.IsInCooldown(TimeSpan.FromMinutes(60), Now).Should().BeFalse();

        alert.ClaimFiring(Now);

        alert.IsInCooldown(TimeSpan.FromMinutes(60), Now.AddMinutes(59)).Should().BeTrue();
        alert.IsInCooldown(TimeSpan.FromMinutes(60), Now.AddMinutes(60)).Should().BeFalse();
        alert.TriggerCount.Should().Be(0);
        alert.Trigger(Now);
        alert.TriggerCount.Should().Be(1);
    }
}
