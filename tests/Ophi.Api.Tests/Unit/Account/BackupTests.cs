using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Api.Features.Account;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Account;

/// <summary>Account backup export (B-1a).</summary>
public class ExportBackupTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _db;
    private readonly Guid _userId = Guid.NewGuid();

    public ExportBackupTests()
    {
        (_db, _connection) = TestDbContextFactory.Create();
    }

    private async Task<(Product product, ProductUrl url)> SeedAccountAsync()
    {
        var user = TestEntityFactory.User(_userId).WithPasswordHash("SECRET-PASSWORD-HASH").Build();
        user.DiscordWebhookUrl = "https://discord.com/api/webhooks/SECRET-DISCORD";
        user.TelegramChatId = "987654321";
        user.PushoverUserKey = "SECRETPUSHOVERKEY0000000000000";
        user.DisplayCurrency = "GBP";
        user.DefaultCheckIntervalMinutes = 120;
        _db.Users.Add(user);

        var tag = new Tag { Id = Guid.NewGuid(), UserId = _userId, Name = "Audio", Color = "#112233", Weight = 2 };
        var group = new ComparisonGroup { Id = Guid.NewGuid(), UserId = _userId, Name = "Headphones", Description = "d" };
        _db.Tags.Add(tag);
        _db.ComparisonGroups.Add(group);
        _db.StoreConfigurations.Add(new StoreConfiguration
        {
            Id = Guid.NewGuid(), UserId = _userId, StoreId = "mystore", Name = "My Store",
            DomainPatternsJson = "[\"mystore.test\"]", SelectorsJson = "{}", AffiliateTag = "aff-1"
        });

        var product = TestEntityFactory.Product(_userId).Named("Sony XM5").Priced(299m, 349m).InGroup(group.Id).Favourite().Build();
        product.CustomFields = [new CustomField("Colour", "Black")];
        var url = TestEntityFactory.ProductUrl(product.Id).WithUrl("https://shop.test/xm5").Priced(299m)
            .LastCheckedAt(new DateTime(2026, 9, 24, 10, 0, 0, DateTimeKind.Utc)).Build();
        _db.Products.Add(product);
        _db.ProductUrls.Add(url);
        _db.ProductTags.Add(new ProductTag { ProductId = product.Id, TagId = tag.Id });
        _db.PricePoints.AddRange(
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = url.Id, Price = 349m, Currency = "USD", RecordedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) },
            new PricePoint { Id = Guid.NewGuid(), ProductId = product.Id, ProductUrlId = url.Id, Price = 299m, Currency = "USD", RecordedAt = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc) });
        var alert = TestEntityFactory.Alert(product.Id, _userId).WithTarget(250m).WithReference(349m)
            .LastTriggered(new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc)).WithTriggerCount(3).Build();
        _db.Alerts.Add(alert);

        _db.ApiKeys.Add(new ApiKey { Id = Guid.NewGuid(), UserId = _userId, Name = "k", KeyHash = "SECRET-API-KEY-HASH" });
        _db.WebhookTargets.Add(new WebhookTarget { Id = Guid.NewGuid(), UserId = _userId, Name = "ha", Url = "https://ha.test/api/webhook/SECRET-HA-ID" });

        // Another user's data must never leak into this user's bundle.
        var other = Guid.NewGuid();
        _db.Users.Add(TestEntityFactory.User(other).Build());
        _db.Products.Add(TestEntityFactory.Product(other).Named("Not Mine").Build());

        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (product, url);
    }

    private Task<BackupBundle> ExportAsync() =>
        new ExportBackup.Handler(_db, TimeProvider.System, NullLogger<ExportBackup.Handler>.Instance)
            .Handle(new ExportBackup.Query(_userId), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Export_CarriesTheWholeAccount()
    {
        var (product, url) = await SeedAccountAsync();

        var bundle = await ExportAsync();

        bundle.Format.Should().Be("ophi-backup");
        bundle.Version.Should().Be(1);
        bundle.Settings.DisplayCurrency.Should().Be("GBP");
        bundle.Settings.DefaultCheckIntervalMinutes.Should().Be(120);
        bundle.Tags.Should().ContainSingle(t => t.Name == "Audio" && t.Color == "#112233");
        bundle.ComparisonGroups.Should().ContainSingle(g => g.Name == "Headphones");
        bundle.Stores.Should().ContainSingle(s => s.StoreId == "mystore" && s.AffiliateTag == "aff-1");

        var p = bundle.Products.Should().ContainSingle().Subject;
        p.Name.Should().Be("Sony XM5");
        p.IsFavourite.Should().BeTrue();
        p.CustomFields.Should().ContainSingle(f => f.Name == "Colour");
        p.TagRefs.Should().ContainSingle().Which.Should().Be(bundle.Tags[0].Ref);
        p.ComparisonGroupRef.Should().Be(bundle.ComparisonGroups[0].Ref);
        p.Urls.Should().ContainSingle(u => u.Url == "https://shop.test/xm5" && u.LastCheckedAt == url.LastCheckedAt);
        p.PriceHistory.Should().HaveCount(2).And.OnlyContain(pp => pp.UrlRef == p.Urls[0].Ref);
        var a = p.Alerts.Should().ContainSingle().Subject;
        a.TargetPrice.Should().Be(250m);
        a.TriggerCount.Should().Be(3);
        a.Condition.Should().Be("below");
    }

    [Fact]
    public async Task Export_NeverWritesCredentials()
    {
        await SeedAccountAsync();

        var json = JsonSerializer.Serialize(await ExportAsync(), BackupBundle.JsonOptions);

        json.Should().NotContain("SECRET-PASSWORD-HASH")
            .And.NotContain("SECRET-DISCORD")
            .And.NotContain("987654321")
            .And.NotContain("SECRETPUSHOVERKEY")
            .And.NotContain("SECRET-API-KEY-HASH")
            .And.NotContain("SECRET-HA-ID");
        json.Should().Contain("\"excluded\"");
    }

    [Fact]
    public async Task Export_OnlyContainsTheCallersData()
    {
        await SeedAccountAsync();

        var bundle = await ExportAsync();

        bundle.Products.Should().NotContain(p => p.Name == "Not Mine");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
