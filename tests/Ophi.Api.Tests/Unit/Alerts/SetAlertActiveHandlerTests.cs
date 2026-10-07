using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Alerts;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Alerts;

public class SetAlertActiveHandlerTests : IDisposable
{
    private const int MaxAlerts = 3;

    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly SetAlertActive.Handler _handler;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Product _product;

    public SetAlertActiveHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        var settings = Options.Create(new AlertSettings { MaxAlertsPerUser = MaxAlerts, CooldownMinutes = 60 });
        _handler = new SetAlertActive.Handler(_dbContext, settings, NullLogger<SetAlertActive.Handler>.Instance);

        _dbContext.Users.Add(TestEntityFactory.User(_userId).Build());
        _product = TestEntityFactory.Product(_userId).Named("Widget").Priced(45m).Build();
        _dbContext.Products.Add(_product);
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task Handle_PausingActiveAlert_PersistsInactiveAndReturnsDto()
    {
        var alert = await SeedAlertAsync(active: true);

        var result = await _handler.Handle(
            new SetAlertActive.Command(alert.Id, false) { UserId = _userId },
            TestContext.Current.CancellationToken);

        result.Active.Should().BeFalse();
        result.ProductName.Should().Be("Widget");
        (await ReloadAsync(alert.Id)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ResumingPausedAlert_PersistsActive()
    {
        var alert = await SeedAlertAsync(active: false);

        var result = await _handler.Handle(
            new SetAlertActive.Command(alert.Id, true) { UserId = _userId },
            TestContext.Current.CancellationToken);

        result.Active.Should().BeTrue();
        (await ReloadAsync(alert.Id)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ResumingWhenAtActiveAlertCap_ThrowsMaxAlertsReached()
    {
        // Without this check, pause -> create -> resume would walk straight past the cap that
        // CreateAlert enforces on active alerts.
        for (var i = 0; i < MaxAlerts; i++) await SeedAlertAsync(active: true);
        var paused = await SeedAlertAsync(active: false);

        var act = async () => await _handler.Handle(
            new SetAlertActive.Command(paused.Id, true) { UserId = _userId },
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ApiException>())
            .Which.Should().Match<ApiException>(e => e.StatusCode == 422 && e.ErrorCode == "MaxAlertsReached");
        (await ReloadAsync(paused.Id)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ResumingAlreadyActiveAlertAtCap_Succeeds()
    {
        // Setting an alert to the state it is already in changes no count, so the cap does not apply.
        var alerts = new List<Alert>();
        for (var i = 0; i < MaxAlerts; i++) alerts.Add(await SeedAlertAsync(active: true));

        var result = await _handler.Handle(
            new SetAlertActive.Command(alerts[0].Id, true) { UserId = _userId },
            TestContext.Current.CancellationToken);

        result.Active.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithAnotherUsersAlert_ThrowsNotFound()
    {
        var otherUserId = Guid.NewGuid();
        _dbContext.Users.Add(TestEntityFactory.User(otherUserId).Build());
        var otherProduct = TestEntityFactory.Product(otherUserId).Named("Other").Build();
        _dbContext.Products.Add(otherProduct);
        var theirs = new Alert
        {
            Id = Guid.NewGuid(), ProductId = otherProduct.Id, UserId = otherUserId,
            TargetPrice = 50m, Condition = AlertCondition.Below, IsActive = true
        };
        _dbContext.Alerts.Add(theirs);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var act = async () => await _handler.Handle(
            new SetAlertActive.Command(theirs.Id, false) { UserId = _userId },
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
        (await ReloadAsync(theirs.Id)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_KeepsTriggerHistory()
    {
        var firedAt = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        var alert = await SeedAlertAsync(active: true, lastTriggered: firedAt);

        var result = await _handler.Handle(
            new SetAlertActive.Command(alert.Id, false) { UserId = _userId },
            TestContext.Current.CancellationToken);

        result.LastTriggered.Should().Be(firedAt);
    }

    private async Task<Alert> SeedAlertAsync(bool active, DateTime? lastTriggered = null)
    {
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            ProductId = _product.Id,
            UserId = _userId,
            TargetPrice = 50m,
            Currency = "USD",
            Condition = AlertCondition.Below,
            IsActive = active,
            LastTriggeredAt = lastTriggered
        };
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return alert;
    }

    private async Task<Alert> ReloadAsync(Guid id)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Alerts.SingleAsync(a => a.Id == id, TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
