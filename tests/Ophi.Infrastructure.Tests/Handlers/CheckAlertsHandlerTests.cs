using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Events;
using Ophi.Infrastructure.Settings;
using Ophi.Infrastructure.Tests.Helpers;
using Ophi.TestHelpers;
using Ophi.Worker.Handlers;

namespace Ophi.Infrastructure.Tests.Handlers;

public class CheckAlertsHandlerTests : HandlerTestBase
{
    private readonly IOptions<AlertSettings> _alertSettings = Options.Create(new AlertSettings { CooldownMinutes = 60, MaxAlertsPerUser = 100 });

    #region Below Condition Tests

    [Fact]
    public async Task HandleAsync_WithBelowConditionMet_ReturnsTriggeredEvent()
    {
        // Arrange
        var product = CreateProduct("Test Product", 49.99m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 49.99m, "USD");

        // Act
        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var triggered = result.ToList();
        triggered.Should().HaveCount(1);
        triggered[0].AlertId.Should().Be(alert.Id);
        triggered[0].ProductId.Should().Be(product.Id);
        triggered[0].CurrentPrice.Should().Be(49.99m);
    }

    [Fact]
    public async Task HandleAsync_WithBelowConditionNotMet_ReturnsEmpty()
    {
        // Arrange
        var product = CreateProduct("Test Product", 75m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 75m, "USD");

        // Act
        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region Above Condition Tests

    [Fact]
    public async Task HandleAsync_WithAboveConditionMet_ReturnsTriggeredEvent()
    {
        // Arrange
        var product = CreateProduct("Test Product", 150m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 125m, AlertCondition.Above);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 150m, "USD");

        // Act
        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        var triggered = result.ToList();
        triggered.Should().HaveCount(1);
        triggered[0].CurrentPrice.Should().Be(150m);
    }

    #endregion

    #region PercentDrop Condition Tests

    [Fact]
    public async Task HandleAsync_WithPercentDropConditionMet_ReturnsTriggeredEvent()
    {
        // Arrange - 20% drop (from 100 to 80)
        var product = CreateProduct("Test Product", 80m, 100m);
        DbContext.Products.Add(product);

        // TargetPrice represents the percentage threshold for PercentDrop
        var alert = CreateAlert(product, 15m, AlertCondition.PercentDrop);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 80m, "USD");

        // Act
        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task HandleAsync_WithPercentDropWhenPriceIncreased_ReturnsEmpty()
    {
        // Arrange — price went UP, so percentChange is negative; alert should not fire
        var product = CreateProduct("Test Product", 120m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 10m, AlertCondition.PercentDrop);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 120m, "USD");

        // Act
        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WithPercentDropButNoPreviousPrice_ReturnsEmpty()
    {
        // Arrange
        var product = CreateProduct("Test Product", 80m, null);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 10m, AlertCondition.PercentDrop);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, null, 80m, "USD");

        // Act
        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    [Fact]
    public async Task HandleAsync_WhenAlertTriggered_PersistsLastTriggeredAt()
    {
        // Arrange
        var product = CreateProduct("Test Product", 40m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Act
        await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert — LastTriggeredAt should be saved to DB to prevent double-trigger race
        var savedAlert = await DbContext.Alerts.FindAsync([alert.Id], TestContext.Current.CancellationToken);
        savedAlert!.LastTriggeredAt.Should().NotBeNull();
        savedAlert.LastTriggeredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #region Cooldown Tests

    [Fact]
    public async Task HandleAsync_WithAlertInCooldown_ReturnsEmpty()
    {
        // Arrange
        var product = CreateProduct("Test Product", 40m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below, lastTriggeredAt: DateTime.UtcNow.AddMinutes(-30)); // 30 min ago, still in cooldown
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Act
        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WithAlertAfterCooldown_ReturnsTriggeredEvent()
    {
        // Arrange
        var product = CreateProduct("Test Product", 40m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below, lastTriggeredAt: DateTime.UtcNow.AddMinutes(-61)); // 61 min ago, past cooldown
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Act
        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().HaveCount(1);
    }

    #endregion

    #region Inactive Alert Tests

    [Fact]
    public async Task HandleAsync_WithInactiveAlert_DoesNotTrigger()
    {
        // Arrange
        var product = CreateProduct("Test Product", 40m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below);
        alert.Pause();
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Act
        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region Multiple Alerts Tests

    [Fact]
    public async Task HandleAsync_WithMultipleAlertsOnSameProduct_TriggersAll()
    {
        // Arrange
        var product = CreateProduct("Test Product", 40m, 100m);
        DbContext.Products.Add(product);

        var alert1 = CreateAlert(product, 50m, AlertCondition.Below);
        var alert2 = CreateAlert(product, 45m, AlertCondition.Below); // Both should trigger with price 40
        DbContext.Alerts.AddRange(alert1, alert2);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Act
        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert — both alerts should trigger
        result.Should().HaveCount(2);
        result.Select(e => e.AlertId).Should().Contain(new[] { alert1.Id, alert2.Id });
    }

    [Fact]
    public async Task HandleAsync_WithMultipleAlertsOnSameProduct_OnlyTriggersMatching()
    {
        // Arrange
        var product = CreateProduct("Test Product", 40m, 100m);
        DbContext.Products.Add(product);

        var alertBelow = CreateAlert(product, 50m, AlertCondition.Below); // Should trigger
        var alertAbove = CreateAlert(product, 150m, AlertCondition.Above); // Should NOT trigger
        DbContext.Alerts.AddRange(alertBelow, alertAbove);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Act
        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert — only below should trigger
        result.Should().HaveCount(1);
        result.First().AlertId.Should().Be(alertBelow.Id);
    }

    [Fact]
    public async Task HandleAsync_WithMultipleAlerts_OneInCooldown_OnlyTriggersNonCooldown()
    {
        // Arrange
        var product = CreateProduct("Test Product", 40m, 100m);
        DbContext.Products.Add(product);

        var alert1 = CreateAlert(product, 50m, AlertCondition.Below, lastTriggeredAt: DateTime.UtcNow.AddMinutes(-30)); // In cooldown

        var alert2 = CreateAlert(product, 60m, AlertCondition.Below);
        // No previous trigger — can fire

        DbContext.Alerts.AddRange(alert1, alert2);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Act
        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert — only alert2 should trigger
        result.Should().HaveCount(1);
        result.First().AlertId.Should().Be(alert2.Id);
    }

    #endregion

    #region Boundary Condition Tests

    [Fact]
    public async Task HandleAsync_WithBelowCondition_FiresWhenPriceEqualsTarget()
    {
        // Arrange — price exactly matches target (should trigger on <=)
        var product = CreateProduct("Test Product", 50m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 50m, "USD");

        // Act
        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task HandleAsync_WithAboveCondition_FiresWhenPriceEqualsTarget()
    {
        // Arrange — price exactly matches target (should trigger on >=)
        var product = CreateProduct("Test Product", 100m, 50m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 100m, AlertCondition.Above);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 50m, 100m, "USD");

        // Act
        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().HaveCount(1);
    }

    #endregion

    #region No Current Price Tests

    [Fact]
    public async Task HandleAsync_WhenProductHasNoCurrentPrice_DoesNotTrigger()
    {
        // Arrange
        var product = CreateProduct("Test Product", null, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Act
        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region No Alerts Tests

    [Fact]
    public async Task HandleAsync_WhenNoAlertsForProduct_ReturnsEmpty()
    {
        // Arrange
        var product = CreateProduct("Test Product", 40m, 100m);
        DbContext.Products.Add(product);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Act
        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region Sequential Double-Fire Prevention Tests

    [Fact]
    public async Task HandleAsync_WhenCalledTwiceForSameEvent_OnlyFiresOnce()
    {
        // Arrange
        var product = CreateProduct("Test Product", 40m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Act — Call handler twice with the same event
        var result1 = (await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken)).ToList();

        var result2 = (await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken)).ToList();

        // Assert
        result1.Should().HaveCount(1, "First call should trigger the alert");
        result2.Should().HaveCount(0, "Second call should return empty due to cooldown (LastTriggeredAt was persisted by first call)");
    }

    [Fact]
    public async Task HandleAsync_LastTriggeredAt_PersistedBeforeReturning()
    {
        // Arrange
        var product = CreateProduct("Test Product", 40m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Act — Call handler once
        var result1 = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert — Immediately after first call, check the DB state
        result1.Should().HaveCount(1, "First call should trigger the alert");

        var alertAfterFirstCall = await DbContext.Alerts.FirstAsync(a => a.Id == alert.Id, TestContext.Current.CancellationToken);
        alertAfterFirstCall.LastTriggeredAt.Should().NotBeNull("LastTriggeredAt should be persisted to DB after trigger");

        // Act — Second call immediately after
        var result2 = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Assert
        result2.Should().BeEmpty("Second call should not trigger because alert.LastTriggeredAt prevents it within cooldown window");
    }

    #endregion

    #region Concurrent Invocation / Race Window

    // CheckAlertsHandler's contract: stamp LastTriggeredAt synchronously (commit before emitting
    // AlertTriggeredEvent) so a fresh DbContext observing the alert sees the cooldown.
    //
    // Production protection against truly parallel races is at the framework level —
    // `LocalQueue("events").MaximumParallelMessages(1)` in Ophi.Worker/Program.cs serializes
    // invocations. These tests document the per-context contract that the queue depends on.

    [Fact]
    public async Task HandleAsync_SequentialCallsWithSeparateContexts_SecondObservesPersistedCooldown()
    {
        // Arrange — seed a product + alert via the shared base-class context.
        var product = CreateProduct("Test Product", 40m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Act — first call uses base context, second call uses a fresh DbContext on the same connection.
        // Without LastTriggeredAt being persisted before the first call returns, the second
        // context's fresh query would still see LastTriggeredAt == null and re-fire.
        var firstResult = (await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken)).ToList();

        await using var secondContext = TestDbContextFactory.Attach(Connection);
        var secondResult = (await CheckAlertsHandler.HandleAsync(
            @event, secondContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken)).ToList();

        // Assert
        firstResult.Should().HaveCount(1, "first call fires the alert");
        secondResult.Should().BeEmpty(
            "second call with a fresh DbContext must observe LastTriggeredAt persisted by the first call");
    }

    [Fact]
    public async Task HandleAsync_ParallelCallsWithSeparateContexts_DoNotThrow()
    {
        // Arrange
        var product = CreateProduct("Test Product", 40m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below);
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Two separate contexts on the same in-memory DB. We can't deterministically force a
        // race-window collision here (Task.WhenAll on a single SQLite connection serializes at
        // the wire protocol), so this test only asserts the no-exception, no-deadlock contract.
        // Whether 1 or 2 AlertTriggeredEvents are emitted depends on interleaving — production
        // queue serialization is what makes "exactly once per cooldown" the real invariant.
        await using var ctxA = TestDbContextFactory.Attach(Connection);
        await using var ctxB = TestDbContextFactory.Attach(Connection);

        var taskA = CheckAlertsHandler.HandleAsync(
            @event, ctxA, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);
        var taskB = CheckAlertsHandler.HandleAsync(
            @event, ctxB, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        // Act
        var results = await Task.WhenAll(taskA, taskB);

        // Assert — at least one invocation must have fired; LastTriggeredAt must be persisted.
        var totalEvents = results.Sum(r => r.Count());
        totalEvents.Should().BeGreaterThanOrEqualTo(1, "at least one parallel invocation must fire");

        await using var verifier = TestDbContextFactory.Attach(Connection);
        var alertAfter = await verifier.Alerts.FirstAsync(a => a.Id == alert.Id, TestContext.Current.CancellationToken);
        alertAfter.LastTriggeredAt.Should().NotBeNull("LastTriggeredAt must be persisted regardless of interleaving");
    }

    [Fact]
    public async Task HandleAsync_FreshContextAfterCooldownExpires_FiresAgain()
    {
        // Arrange — alert was triggered well outside the cooldown window.
        var product = CreateProduct("Test Product", 40m, 100m);
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 50m, AlertCondition.Below, lastTriggeredAt: DateTime.UtcNow.AddHours(-2));
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 40m, "USD");

        // Act — invoke through a fresh context that has never seen the alert before.
        await using var freshContext = TestDbContextFactory.Attach(Connection);
        var result = (await CheckAlertsHandler.HandleAsync(
            @event, freshContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken)).ToList();

        // Assert
        result.Should().HaveCount(1, "fresh context still re-evaluates cooldown from the DB-persisted timestamp");
    }

    #endregion

    #region Currency Guard Tests

    [Fact]
    public async Task HandleAsync_WhenProductReanchoredToAnotherCurrency_DoesNotTrigger()
    {
        // The non-US-egress repro end to end: a product tracked in USD gets re-anchored to EUR by
        // ProductPriceAggregator, and EUR 75 numerically clears a target that was set as USD 80.
        // Firing here would email the user "€75, below your €80 target" — a threshold they never set.
        var product = CreateProduct("Re-anchored Product", 75m, 100m, currency: "EUR");
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 80m, AlertCondition.Below, currency: "USD");
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(
            product.Id, 100m, 75m, "EUR", OldCurrency: "USD");

        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenCurrencyMismatched_DoesNotConsumeTheCooldown()
    {
        // A dormant alert must stay pristine: stamping LastTriggeredAt for an alert that never fired
        // would put it in cooldown, so the first legitimate trigger after the currencies realign
        // would be swallowed too.
        var product = CreateProduct("Re-anchored Product", 75m, 100m, currency: "EUR");
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 80m, AlertCondition.Below, currency: "USD");
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 75m, "EUR", OldCurrency: "USD");

        await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        var reloaded = await DbContext.Alerts.AsNoTracking()
            .FirstAsync(a => a.Id == alert.Id, TestContext.Current.CancellationToken);
        reloaded.LastTriggeredAt.Should().BeNull();
        reloaded.TriggerCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_WhenCurrenciesAgreeAgain_TriggersNormally()
    {
        // The guard is dormancy, not a permanent kill: once the product is priced in the alert's
        // currency again the alert resumes firing.
        var product = CreateProduct("Recovered Product", 75m, 100m, currency: "USD");
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 80m, AlertCondition.Below, currency: "USD");
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 75m, "USD", OldCurrency: "USD");

        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task HandleAsync_PercentDropSurvivesReanchoringWhenTheDropIsWithinOneCurrency()
    {
        // A percent-drop target is a percentage, so it stays meaningful whatever the product is
        // denominated in — it must NOT go dormant just because the alert was created while the
        // product was in USD. What matters is that this scrape's before/after share a currency.
        var product = CreateProduct("Euro Product", 75m, 100m, currency: "EUR");
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 20m, AlertCondition.PercentDrop, currency: "USD");
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 75m, "EUR", OldCurrency: "EUR");

        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        result.Should().HaveCount(1, "a 25% drop from EUR 100 to EUR 75 is a real 25% drop");
    }

    [Fact]
    public async Task HandleAsync_PercentDropAcrossACurrencyChange_DoesNotTrigger()
    {
        // Same alert and product currencies, so the headline guard passes — but the drop itself is
        // computed from USD 100 to EUR 75, which is a re-denomination, not a 25% price cut.
        var product = CreateProduct("Flipped Product", 75m, 100m, currency: "EUR");
        DbContext.Products.Add(product);

        var alert = CreateAlert(product, 20m, AlertCondition.PercentDrop, currency: "EUR");
        DbContext.Alerts.Add(alert);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var @event = new PriceUpdatedEvent(product.Id, 100m, 75m, "EUR", OldCurrency: "USD");

        var result = await CheckAlertsHandler.HandleAsync(
            @event, DbContext, _alertSettings, TimeProvider.System, LoggerMock.Object, TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }

    #endregion

    #region Helper Methods

    private Product CreateProduct(
        string name, decimal? currentPrice, decimal? previousPrice, string currency = "USD")
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            UserId = TestUserId,
            Name = name,
            CurrentPrice = currentPrice,
            PreviousPrice = previousPrice,
            Currency = currency,
            Status = ProductStatus.Active
        };
    }

    private Alert CreateAlert(
        Product product, decimal targetPrice, AlertCondition condition, string currency = "USD",
        DateTime? lastTriggeredAt = null)
    {
        return new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Product = product,
            UserId = TestUserId,
            User = TestUser,
            TargetPrice = targetPrice,
            Currency = currency,
            Condition = condition,
            IsActive = true,
            TriggerCount = 0,
            LastTriggeredAt = lastTriggeredAt
        };
    }

    #endregion
}
