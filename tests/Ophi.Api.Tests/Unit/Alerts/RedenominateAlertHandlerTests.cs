using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Alerts;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Alerts;

public class RedenominateAlertHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly RedenominateAlert.Handler _handler;
    private readonly Guid _testUserId = Guid.NewGuid();
    private readonly Guid _testProductId = Guid.NewGuid();

    public RedenominateAlertHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new RedenominateAlert.Handler(
            _dbContext, NullLogger<RedenominateAlert.Handler>.Instance);

        _dbContext.Users.Add(new User
        {
            Id = _testUserId,
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hash"
        });
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task Handle_OnADormantAlert_AdoptsTheProductsCurrencyAndTheNewTarget()
    {
        // The product re-anchored to EUR; the alert's USD target went dormant with it.
        var alert = await SeedDormantAlert(alertCurrency: "USD", productCurrency: "EUR");

        var result = await _handler.Handle(
            Command(alert.Id, targetPrice: 69m, expected: "EUR"), TestContext.Current.CancellationToken);

        result.Currency.Should().Be("EUR");
        result.TargetPrice.Should().Be(69m);
        result.HasCurrencyMismatch.Should().BeFalse("re-denominating is what ends dormancy");

        var saved = await _dbContext.Alerts.FindAsync([alert.Id], TestContext.Current.CancellationToken);
        saved!.Currency.Should().Be("EUR");
        saved.TargetPrice.Should().Be(69m);
    }

    [Fact]
    public async Task Handle_DoesNotCarryTheOldTargetForward()
    {
        // 80 USD is not 80 EUR. Silently reusing the number is the bug the dormancy rule prevents,
        // and it would be a regression to reintroduce it on the recovery path.
        var alert = await SeedDormantAlert(alertCurrency: "USD", productCurrency: "EUR", target: 80m);

        var result = await _handler.Handle(
            Command(alert.Id, targetPrice: 69m, expected: "EUR"), TestContext.Current.CancellationToken);

        result.TargetPrice.Should().NotBe(80m);
        result.TargetPrice.Should().Be(69m);
    }

    [Fact]
    public async Task Handle_OnALiveAlert_Throws()
    {
        // Guards the boundary: this endpoint recovers dormant alerts, it is not an UpdateAlert.
        var alert = await SeedDormantAlert(alertCurrency: "EUR", productCurrency: "EUR");

        var act = async () => await _handler.Handle(
            Command(alert.Id, targetPrice: 69m, expected: "EUR"), TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<ApiException>();
        thrown.Which.StatusCode.Should().Be(422);
        thrown.Which.ErrorCode.Should().Be("AlertNotDormant");
    }

    [Fact]
    public async Task Handle_WhenTheProductRepricedSinceTheUserLoadedThePage_Throws()
    {
        // The user typed a number while looking at EUR; by submit time the product had re-anchored
        // to GBP. Stamping their EUR number as GBP would be the very re-denomination this feature
        // exists to undo, so refuse and make them look at the new currency first.
        var alert = await SeedDormantAlert(alertCurrency: "USD", productCurrency: "GBP");

        var act = async () => await _handler.Handle(
            Command(alert.Id, targetPrice: 69m, expected: "EUR"), TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<ApiException>();
        thrown.Which.StatusCode.Should().Be(422);
        thrown.Which.ErrorCode.Should().Be("CurrencyChanged");
    }

    [Fact]
    public async Task Handle_LeavesTriggerHistoryIntact()
    {
        var firedAt = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var alert = await SeedDormantAlert(alertCurrency: "USD", productCurrency: "EUR");
        alert.Trigger(firedAt);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _handler.Handle(
            Command(alert.Id, targetPrice: 69m, expected: "EUR"), TestContext.Current.CancellationToken);

        result.LastTriggered.Should().Be(firedAt);
    }

    [Fact]
    public async Task Handle_WithAnAlertBelongingToAnotherUser_ThrowsNotFound()
    {
        var otherUserId = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = otherUserId,
            Email = "other@example.com",
            Name = "Other User",
            PasswordHash = "hash"
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var alert = await SeedDormantAlert(
            alertCurrency: "USD", productCurrency: "EUR", ownerId: otherUserId);

        var act = async () => await _handler.Handle(
            Command(alert.Id, targetPrice: 69m, expected: "EUR"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>().WithMessage("Alert not found");
    }

    [Fact]
    public async Task Handle_WithAnUnknownAlert_ThrowsNotFound()
    {
        var act = async () => await _handler.Handle(
            Command(Guid.NewGuid(), targetPrice: 69m, expected: "EUR"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>().WithMessage("Alert not found");
    }

    private RedenominateAlert.Command Command(Guid alertId, decimal targetPrice, string expected) =>
        new(alertId, targetPrice, expected) { UserId = _testUserId };

    private async Task<Alert> SeedDormantAlert(
        string alertCurrency, string productCurrency, decimal target = 80m, Guid? ownerId = null)
    {
        var product = TestEntityFactory.Product(_testUserId)
            .WithId(_testProductId).Named("Sennheiser HD 600")
            .Priced(120.00m).WithCurrency(productCurrency).Build();
        _dbContext.Products.Add(product);

        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = _testProductId,
            UserId = ownerId ?? _testUserId,
            TargetPrice = target,
            ReferencePrice = 120.00m,
            Currency = alertCurrency,
            Condition = AlertCondition.Below,
            IsActive = true
        };
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return alert;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
