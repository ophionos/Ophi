using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Account;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Account;

/// <summary>Account backup import (B-1b): merge-only, new IDs, credentials never expected.</summary>
public class ImportBackupTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _db;
    private readonly Guid _userId = Guid.NewGuid();

    public ImportBackupTests()
    {
        (_db, _connection) = TestDbContextFactory.Create();
        _db.Users.Add(TestEntityFactory.User(_userId).Build());
        _db.SaveChanges();
    }

    private static readonly Guid TagRef = Guid.NewGuid();
    private static readonly Guid GroupRef = Guid.NewGuid();
    private static readonly Guid UrlRef = Guid.NewGuid();

    private static BackupProduct Product(string name, string url, params BackupAlert[] alerts) => new(
        name, null, 299m, 349m, "USD", "active", true, 60,
        [new BackupCustomField("Colour", "Black")],
        [TagRef], GroupRef,
        [new BackupProductUrl(UrlRef, url, null, 299m, "USD", new DateTime(2026, 9, 24, 10, 0, 0, DateTimeKind.Utc), "active", false, null, "auto")],
        [
            new BackupPricePoint(UrlRef, 349m, "USD", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)),
            new BackupPricePoint(UrlRef, 299m, "USD", new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc))
        ],
        alerts);

    private static BackupAlert Alert(bool active = true) =>
        new("below", 250m, 349m, "USD", active, new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), 3);

    private static BackupBundle Bundle(params BackupProduct[] products) => new(
        "ophi-backup", 1, DateTime.UtcNow, BackupBundle.ExcludedItems,
        new BackupSettings(false, 120, 5, 30, 40, 7, false, "GBP"),
        [new BackupTag(TagRef, "Audio", "#112233", 2)],
        [new BackupComparisonGroup(GroupRef, "Headphones", "d")],
        [new BackupStore("mystore", "My Store", "[\"mystore.test\"]", "{}", "en-US", false, null, null, "aff-1", null)],
        products);

    private Task<ImportBackup.Response> ImportAsync(BackupBundle bundle, int maxAlerts = 100) =>
        new ImportBackup.Handler(_db, Options.Create(new AlertSettings { MaxAlertsPerUser = maxAlerts }),
                NullLogger<ImportBackup.Handler>.Instance)
            .Handle(new ImportBackup.Command(bundle) { UserId = _userId }, TestContext.Current.CancellationToken);

    private OphiDbContext Fresh()
    {
        _db.ChangeTracker.Clear();
        return _db;
    }

    [Fact]
    public async Task Import_IntoAnEmptyAccount_RestoresEverythingWithNewIds()
    {
        var result = await ImportAsync(Bundle(Product("Sony XM5", "https://shop.test/xm5", Alert())));

        result.ProductsAdded.Should().Be(1);
        result.PricePointsAdded.Should().Be(2);
        result.AlertsAdded.Should().Be(1);
        result.SettingsRestored.Should().BeTrue();

        var db = Fresh();
        var product = await db.Products.Include(p => p.ProductUrls).Include(p => p.ProductTags).ThenInclude(pt => pt.Tag)
            .Include(p => p.Alerts).Include(p => p.ComparisonGroup)
            .SingleAsync(p => p.UserId == _userId, TestContext.Current.CancellationToken);
        product.Name.Should().Be("Sony XM5");
        product.IsFavourite.Should().BeTrue();
        product.CustomFields.Should().ContainSingle(f => f.Name == "Colour");
        product.ProductTags.Should().ContainSingle(pt => pt.Tag.Name == "Audio" && pt.TagId != TagRef);
        product.ComparisonGroup!.Name.Should().Be("Headphones");
        product.ComparisonGroup.Id.Should().NotBe(GroupRef);
        var url = product.ProductUrls.Should().ContainSingle().Subject;
        url.Id.Should().NotBe(UrlRef, "bundle ids are references only");
        url.LastCheckedAt.Should().Be(new DateTime(2026, 9, 24, 10, 0, 0, DateTimeKind.Utc),
            "keeping lastCheckedAt avoids re-scraping every imported URL at once");
        (await db.PricePoints.Where(pp => pp.ProductId == product.Id).ToListAsync(TestContext.Current.CancellationToken))
            .Should().HaveCount(2).And.OnlyContain(pp => pp.ProductUrlId == url.Id);
        var alert = product.Alerts.Should().ContainSingle().Subject;
        alert.TriggerCount.Should().Be(3);
        alert.UserId.Should().Be(_userId);

        var user = await db.Users.SingleAsync(u => u.Id == _userId, TestContext.Current.CancellationToken);
        user.DisplayCurrency.Should().Be("GBP");
        user.DefaultCheckIntervalMinutes.Should().Be(120);
        (await db.StoreConfigurations.SingleAsync(s => s.UserId == _userId, TestContext.Current.CancellationToken))
            .AffiliateTag.Should().Be("aff-1");
    }

    [Fact]
    public async Task Import_Merges_ReusingTagsAndGroups_KeepingStores_SkippingTrackedUrls()
    {
        var existingTag = new Tag { Id = Guid.NewGuid(), UserId = _userId, Name = "audio", Color = "#000000" };
        _db.Tags.Add(existingTag);
        _db.StoreConfigurations.Add(new StoreConfiguration { Id = Guid.NewGuid(), UserId = _userId, StoreId = "mystore", Name = "Mine, edited" });
        var tracked = TestEntityFactory.Product(_userId).Named("Already here").Build();
        _db.Products.Add(tracked);
        _db.ProductUrls.Add(TestEntityFactory.ProductUrl(tracked.Id).WithUrl("https://shop.test/xm5").Build());
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await ImportAsync(Bundle(
            Product("Sony XM5", "https://shop.test/xm5"),
            Product("Bose QC", "https://shop.test/qc")));

        result.ProductsAdded.Should().Be(1);
        result.ProductsSkipped.Should().Be(1);
        result.TagsAdded.Should().Be(0);
        result.StoresKept.Should().Be(1);

        var db = Fresh();
        (await db.Tags.CountAsync(t => t.UserId == _userId, TestContext.Current.CancellationToken)).Should().Be(1);
        (await db.StoreConfigurations.SingleAsync(s => s.UserId == _userId, TestContext.Current.CancellationToken))
            .Name.Should().Be("Mine, edited", "an existing store is kept, never overwritten");
        var bose = await db.Products.Include(p => p.ProductTags)
            .SingleAsync(p => p.Name == "Bose QC", TestContext.Current.CancellationToken);
        bose.ProductTags.Should().ContainSingle(pt => pt.TagId == existingTag.Id);
    }

    [Fact]
    public async Task Import_IntoAnAccountWithProducts_LeavesItsSettingsAlone()
    {
        // Merge-only covers settings too: restoring one deleted product must not, say, turn email
        // back on for someone who switched it off after taking the backup.
        var user = _db.Users.Single(u => u.Id == _userId);
        user.EmailNotificationsEnabled = true;
        user.DisplayCurrency = null;
        var existing = TestEntityFactory.Product(_userId).Named("Existing").Build();
        _db.Products.Add(existing);
        _db.ProductUrls.Add(TestEntityFactory.ProductUrl(existing.Id).WithUrl("https://shop.test/existing").Build());
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await ImportAsync(Bundle(Product("Sony XM5", "https://shop.test/xm5")));

        result.SettingsRestored.Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Contains("Settings were left unchanged"));
        var after = Fresh().Users.Single(u => u.Id == _userId);
        after.EmailNotificationsEnabled.Should().BeTrue();
        after.DisplayCurrency.Should().BeNull();
    }

    [Fact]
    public async Task Import_PausesActiveAlertsBeyondTheCap_AndSaysSo()
    {
        var result = await ImportAsync(
            Bundle(Product("A", "https://shop.test/a", Alert(), Alert()), Product("B", "https://shop.test/b", Alert(active: false))),
            maxAlerts: 1);

        result.AlertsAdded.Should().Be(3);
        result.AlertsPaused.Should().Be(1);
        result.Warnings.Should().Contain(w => w.Contains("paused"));
        var db = Fresh();
        (await db.Alerts.CountAsync(a => a.UserId == _userId && a.IsActive, TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task Import_SkipsProductsWithoutAValidUrl_WithAWarning()
    {
        var result = await ImportAsync(Bundle(Product("Broken", "javascript:alert(1)")));

        result.ProductsAdded.Should().Be(0);
        result.Warnings.Should().ContainSingle(w => w.Contains("Broken"));
    }

    [Theory]
    [InlineData("something-else", 1)]
    [InlineData("ophi-backup", 2)]
    public async Task Import_RefusesAnUnknownFormatOrVersion(string format, int version)
    {
        var act = () => ImportAsync(Bundle() with { Format = format, Version = version });

        (await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(400);
        (await Fresh().Tags.AnyAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task Import_RefusesAFileWithMissingSections_As400NotACrash()
    {
        // What System.Text.Json yields for {"format":"ophi-backup","version":1} — lists left null.
        var act = () => ImportAsync(Bundle() with { Products = null!, Tags = null!, Settings = null! });

        (await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(400);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
