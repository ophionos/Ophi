using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ophi.Api.Features.Account;
using Ophi.Api.Features.Settings;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Shipping;

/// <summary>The landed-price setting and the shipping rates in the account backup (#144).</summary>
public class ShippingSettingsAndBackupTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _db;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _targetId = Guid.NewGuid();

    public ShippingSettingsAndBackupTests()
    {
        (_db, _connection) = TestDbContextFactory.Create();
        _db.Users.Add(TestEntityFactory.User(_userId).Build());
        _db.Users.Add(TestEntityFactory.User(_targetId).Build());
        _db.SaveChanges();
    }

    private UpdateSettings.Handler UpdateHandler() => new(_db, NullLogger<UpdateSettings.Handler>.Instance);

    private Task<ImportBackup.Response> ImportAsync(BackupBundle bundle) =>
        new ImportBackup.Handler(_db, Options.Create(new AlertSettings()), NullLogger<ImportBackup.Handler>.Instance)
            .Handle(new ImportBackup.Command(bundle) { UserId = _targetId }, TestContext.Current.CancellationToken);

    private Task<BackupBundle> ExportAsync() =>
        new ExportBackup.Handler(_db, TimeProvider.System, NullLogger<ExportBackup.Handler>.Instance)
            .Handle(new ExportBackup.Query(_userId), TestContext.Current.CancellationToken);

    private void SeedRate(Guid userId, string host, decimal amount, string currency, decimal? freeAbove = null)
    {
        _db.ShippingRates.Add(new ShippingRate
        {
            Id = Guid.NewGuid(), UserId = userId, Host = host, Amount = amount, Currency = currency, FreeAbove = freeAbove
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task UpdateSettings_WithShowLandedPrices_PersistsAndReturnsIt()
    {
        var result = await UpdateHandler().Handle(
            new UpdateSettings.Command(null, null, ShowLandedPrices: true) { UserId = _userId },
            TestContext.Current.CancellationToken);

        result.ShowLandedPrices.Should().BeTrue();
        (await _db.Users.AsNoTracking().SingleAsync(u => u.Id == _userId, TestContext.Current.CancellationToken))
            .ShowLandedPrices.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateSettings_WithNullShowLandedPrices_LeavesItUnchanged()
    {
        await UpdateHandler().Handle(
            new UpdateSettings.Command(null, null, ShowLandedPrices: true) { UserId = _userId },
            TestContext.Current.CancellationToken);

        var result = await UpdateHandler().Handle(
            new UpdateSettings.Command(AffiliatesEnabled: false, DefaultCheckIntervalMinutes: null) { UserId = _userId },
            TestContext.Current.CancellationToken);

        result.ShowLandedPrices.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateSettings_CanTurnShowLandedPricesOffAgain()
    {
        await UpdateHandler().Handle(
            new UpdateSettings.Command(null, null, ShowLandedPrices: true) { UserId = _userId },
            TestContext.Current.CancellationToken);

        var result = await UpdateHandler().Handle(
            new UpdateSettings.Command(null, null, ShowLandedPrices: false) { UserId = _userId },
            TestContext.Current.CancellationToken);

        result.ShowLandedPrices.Should().BeFalse();
    }

    [Fact]
    public async Task GetSettings_ForNewUser_ReportsShowLandedPricesOff()
    {
        var handler = new GetSettings.Handler(_db, Options.Create(new Ophi.Infrastructure.Email.EmailSettings()),
            new Moq.Mock<Ophi.Infrastructure.Push.ITelegramService>().Object,
            new Moq.Mock<Ophi.Infrastructure.Push.IPushoverService>().Object,
            NullLogger<GetSettings.Handler>.Instance);

        var result = await handler.Handle(new GetSettings.Query { UserId = _userId }, TestContext.Current.CancellationToken);

        result.ShowLandedPrices.Should().BeFalse();
    }

    [Fact]
    public async Task Export_IncludesRatesOrderedByHostAndTheSetting()
    {
        (await _db.Users.SingleAsync(u => u.Id == _userId, TestContext.Current.CancellationToken)).ShowLandedPrices = true;
        SeedRate(_userId, "zalando.de", 3m, "EUR", 50m);
        SeedRate(_userId, "amazon.es", 4.99m, "EUR");
        SeedRate(_targetId, "other.test", 1m, "USD");
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var bundle = await ExportAsync();

        bundle.Settings.ShowLandedPrices.Should().BeTrue();
        bundle.ShippingRates.Should().Equal(
            new BackupShippingRate("amazon.es", 4.99m, "EUR", null),
            new BackupShippingRate("zalando.de", 3m, "EUR", 50m));
    }

    [Fact]
    public async Task ExportThenImport_ThroughJson_RestoresRatesAndTheSetting()
    {
        (await _db.Users.SingleAsync(u => u.Id == _userId, TestContext.Current.CancellationToken)).ShowLandedPrices = true;
        SeedRate(_userId, "amazon.es", 4.99m, "EUR", 29m);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var json = JsonSerializer.Serialize(await ExportAsync(), BackupBundle.JsonOptions);
        var bundle = JsonSerializer.Deserialize<BackupBundle>(json, BackupBundle.JsonOptions)!;

        var result = await ImportAsync(bundle);

        result.SettingsRestored.Should().BeTrue();
        result.ShippingRatesRestored.Should().Be(1);
        _db.ChangeTracker.Clear();
        (await _db.Users.SingleAsync(u => u.Id == _targetId, TestContext.Current.CancellationToken)).ShowLandedPrices.Should().BeTrue();
        var rate = await _db.ShippingRates.SingleAsync(r => r.UserId == _targetId, TestContext.Current.CancellationToken);
        (rate.Host, rate.Amount, rate.Currency, rate.FreeAbove).Should().Be(("amazon.es", 4.99m, "EUR", 29m));
    }

    [Fact]
    public async Task Import_WithExistingHost_ReplacesThatRateAndKeepsOthers()
    {
        SeedRate(_targetId, "amazon.es", 9m, "GBP", 100m);
        SeedRate(_targetId, "keep.test", 2m, "USD");
        var bundle = EmptyBundle([new BackupShippingRate("amazon.es", 4.99m, "EUR", null)]);

        await ImportAsync(bundle);

        _db.ChangeTracker.Clear();
        var rows = await _db.ShippingRates.Where(r => r.UserId == _targetId).OrderBy(r => r.Host).ToListAsync(TestContext.Current.CancellationToken);
        rows.Select(r => (r.Host, r.Amount, r.Currency, r.FreeAbove)).Should().Equal(
            ("amazon.es", 4.99m, "EUR", (decimal?)null),
            ("keep.test", 2m, "USD", (decimal?)null));
    }

    [Fact]
    public async Task Import_WithInvalidRates_SkipsThemWithAWarning()
    {
        var bundle = EmptyBundle(
        [
            new BackupShippingRate("ok.test", 1m, "eur", null),
            new BackupShippingRate("bad/host", 1m, "EUR", null),
            new BackupShippingRate("neg.test", -1m, "EUR", null),
            new BackupShippingRate("cur.test", 1m, "EURO", null),
            new BackupShippingRate("free.test", 1m, "EUR", 0m)
        ]);

        var result = await ImportAsync(bundle);

        result.ShippingRatesRestored.Should().Be(1);
        result.Warnings.Should().HaveCount(4);
        _db.ChangeTracker.Clear();
        (await _db.ShippingRates.SingleAsync(r => r.UserId == _targetId, TestContext.Current.CancellationToken))
            .Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task Import_OfAnOlderFileWithoutTheNewMembers_StillWorks()
    {
        const string json = """
            {"format":"ophi-backup","version":1,"exportedAt":"2026-09-24T00:00:00Z","excluded":[],
             "settings":{"affiliatesEnabled":true,"defaultCheckIntervalMinutes":null,"pageFetchDelaySeconds":null,
               "scrapeCacheTtlMinutes":null,"anomalyThresholdPercent":null,"autoPauseAfterFailures":null,
               "emailNotificationsEnabled":true,"displayCurrency":null},
             "tags":[],"comparisonGroups":[],"stores":[],"products":[]}
            """;
        var bundle = JsonSerializer.Deserialize<BackupBundle>(json, BackupBundle.JsonOptions)!;

        var result = await ImportAsync(bundle);

        result.SettingsRestored.Should().BeTrue();
        result.ShippingRatesRestored.Should().Be(0);
        (await _db.Users.AsNoTracking().SingleAsync(u => u.Id == _targetId, TestContext.Current.CancellationToken))
            .ShowLandedPrices.Should().BeFalse();
    }

    private static BackupBundle EmptyBundle(IReadOnlyList<BackupShippingRate> rates) => new(
        "ophi-backup", 1, DateTime.UtcNow, BackupBundle.ExcludedItems,
        new BackupSettings(true, null, null, null, null, null, true, null),
        [], [], [], [], rates);

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
