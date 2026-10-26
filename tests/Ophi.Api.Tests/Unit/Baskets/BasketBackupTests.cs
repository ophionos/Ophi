using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ophi.Api.Features.Account;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Baskets;

/// <summary>Baskets in the account backup: export, import, merge by name and product mapping (#147).</summary>
public class BasketBackupTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _db;
    private readonly Guid _sourceId = Guid.NewGuid();
    private readonly Guid _targetId = Guid.NewGuid();

    public BasketBackupTests()
    {
        (_db, _connection) = TestDbContextFactory.Create();
        _db.Users.Add(TestEntityFactory.User(_sourceId).Build());
        _db.Users.Add(TestEntityFactory.User(_targetId).Build());
        _db.SaveChanges();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<BackupBundle> ExportAsync(Guid userId) =>
        new ExportBackup.Handler(_db, TimeProvider.System, NullLogger<ExportBackup.Handler>.Instance)
            .Handle(new ExportBackup.Query(userId), Ct);

    private Task<ImportBackup.Response> ImportAsync(BackupBundle bundle) =>
        new ImportBackup.Handler(_db, Options.Create(new AlertSettings()), NullLogger<ImportBackup.Handler>.Instance)
            .Handle(new ImportBackup.Command(bundle) { UserId = _targetId }, Ct);

    private async Task<Product> SeedProductAsync(Guid userId, string name, string url)
    {
        var product = TestEntityFactory.Product(userId).Named(name).Build();
        _db.Products.Add(product);
        _db.ProductUrls.Add(TestEntityFactory.ProductUrl(product.Id).WithUrl(url).Build());
        await _db.SaveChangesAsync(Ct);
        return product;
    }

    private async Task<Basket> SeedBasketAsync(Guid userId, string name, string? description, params (Product Product, int Quantity)[] items)
    {
        var basket = new Basket { Id = Guid.NewGuid(), UserId = userId, Name = name, Description = description };
        foreach (var (product, quantity) in items) basket.AddItem(product.Id, quantity);
        _db.Baskets.Add(basket);
        await _db.SaveChangesAsync(Ct);
        return basket;
    }

    private static BackupProduct BundleProduct(Guid? @ref, string name, string url) => new(
        name, null, null, null, "USD", "active", false, null, [], [], null,
        [new BackupProductUrl(Guid.NewGuid(), url, null, null, "USD", null, "active", false, null, "auto")],
        [], [], @ref);

    private static BackupBundle Bundle(IReadOnlyList<BackupProduct> products, IReadOnlyList<BackupBasket>? baskets) => new(
        "ophi-backup", 1, DateTime.UtcNow, BackupBundle.ExcludedItems,
        new BackupSettings(true, null, null, null, null, null, true, null),
        [], [], [], products, null, baskets);

    private static BackupBundle RoundTrip(BackupBundle bundle) =>
        JsonSerializer.Deserialize<BackupBundle>(JsonSerializer.Serialize(bundle, BackupBundle.JsonOptions), BackupBundle.JsonOptions)!;

    [Fact]
    public async Task Export_IncludesOwnBasketsOrderedByNameWithProductRefsAndQuantities()
    {
        var a = await SeedProductAsync(_sourceId, "A", "https://shop.test/a");
        var b = await SeedProductAsync(_sourceId, "B", "https://shop.test/b");
        var theirs = await SeedProductAsync(_targetId, "T", "https://shop.test/t");
        await SeedBasketAsync(_sourceId, "Zeta", null, (b, 1));
        await SeedBasketAsync(_sourceId, "Alpha", "First", (a, 2), (b, 7));
        await SeedBasketAsync(_targetId, "Other", null, (theirs, 1));

        var bundle = await ExportAsync(_sourceId);

        bundle.Baskets.Should().NotBeNull();
        bundle.Baskets!.Select(x => x.Name).Should().Equal("Alpha", "Zeta");
        var alpha = bundle.Baskets[0];
        alpha.Description.Should().Be("First");
        alpha.Items.Select(i => (i.ProductRef, i.Quantity)).Should().BeEquivalentTo([(a.Id, 2), (b.Id, 7)]);
        bundle.Products.Select(p => p.Ref).Should().BeEquivalentTo([(Guid?)a.Id, b.Id]);
    }

    [Fact]
    public async Task ExportThenImport_ThroughJson_RestoresBasketsNamesAndQuantities()
    {
        var a = await SeedProductAsync(_sourceId, "Monitor", "https://shop.test/monitor");
        var b = await SeedProductAsync(_sourceId, "Keyboard", "https://shop.test/keyboard");
        await SeedBasketAsync(_sourceId, "Desk", "Home office", (a, 2), (b, 3));
        await SeedBasketAsync(_sourceId, "Empty", null);
        var bundle = RoundTrip(await ExportAsync(_sourceId));

        var result = await ImportAsync(bundle);

        result.BasketsRestored.Should().Be(2);
        _db.ChangeTracker.Clear();
        var baskets = await _db.Baskets.Include(x => x.Items).ThenInclude(i => i.Product)
            .Where(x => x.UserId == _targetId).OrderBy(x => x.Name).ToListAsync(Ct);
        baskets.Select(x => x.Name).Should().Equal("Desk", "Empty");
        baskets[0].Description.Should().Be("Home office");
        baskets[0].Items.Select(i => (i.Product.Name, i.Product.UserId, i.Quantity))
            .Should().BeEquivalentTo([("Monitor", _targetId, 2), ("Keyboard", _targetId, 3)]);
        baskets[1].Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_WithItemWhoseProductIsNotInTheFile_SkipsTheItemWithAWarning()
    {
        var bundle = Bundle(
            [BundleProduct(Guid.NewGuid(), "Kept", "https://shop.test/kept")],
            [new BackupBasket("Desk", null, [new BackupBasketItem(Guid.NewGuid(), 1)])]);

        var result = await ImportAsync(bundle);

        result.BasketsRestored.Should().Be(1);
        result.Warnings.Should().ContainSingle(w => w.Contains("basket", StringComparison.OrdinalIgnoreCase));
        _db.ChangeTracker.Clear();
        (await _db.BasketItems.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Import_WithExistingBasketName_MergesItemsAndKeepsExistingQuantities()
    {
        var existing = await SeedProductAsync(_targetId, "Existing", "https://shop.test/existing");
        await SeedBasketAsync(_targetId, "desk", "Mine", (existing, 5));
        var refExisting = Guid.NewGuid();
        var refNew = Guid.NewGuid();
        var bundle = Bundle(
            [BundleProduct(refExisting, "Existing", "https://shop.test/existing"), BundleProduct(refNew, "New", "https://shop.test/new")],
            [new BackupBasket("Desk", "Theirs", [new BackupBasketItem(refExisting, 1), new BackupBasketItem(refNew, 2)])]);

        var result = await ImportAsync(bundle);

        result.BasketsRestored.Should().Be(1);
        _db.ChangeTracker.Clear();
        var basket = await _db.Baskets.Include(x => x.Items).ThenInclude(i => i.Product).SingleAsync(x => x.UserId == _targetId, Ct);
        basket.Name.Should().Be("desk");
        basket.Description.Should().Be("Mine");
        basket.Items.Select(i => (i.Product.Name, i.Quantity)).Should().BeEquivalentTo([("Existing", 5), ("New", 2)]);
    }

    [Fact]
    public async Task Import_WithProductAlreadyTracked_PutsTheExistingProductInTheBasket()
    {
        var tracked = await SeedProductAsync(_targetId, "Tracked", "https://shop.test/tracked");
        var productRef = Guid.NewGuid();
        var bundle = Bundle(
            [BundleProduct(productRef, "Tracked", "https://shop.test/tracked")],
            [new BackupBasket("Desk", null, [new BackupBasketItem(productRef, 4)])]);

        var result = await ImportAsync(bundle);

        result.ProductsSkipped.Should().Be(1);
        _db.ChangeTracker.Clear();
        var item = await _db.BasketItems.SingleAsync(Ct);
        (item.ProductId, item.Quantity).Should().Be((tracked.Id, 4));
    }

    [Fact]
    public async Task Import_WithInvalidBasketsAndQuantities_SkipsThemWithWarnings()
    {
        var productRef = Guid.NewGuid();
        var bundle = Bundle(
            [BundleProduct(productRef, "P", "https://shop.test/p")],
            [
                new BackupBasket("", null, []),
                new BackupBasket(new string('x', 101), null, []),
                new BackupBasket("Good", null,
                [
                    new BackupBasketItem(productRef, 0),
                    new BackupBasketItem(productRef, 1000)
                ])
            ]);

        var result = await ImportAsync(bundle);

        result.BasketsRestored.Should().Be(1);
        result.Warnings.Should().HaveCount(3);
        _db.ChangeTracker.Clear();
        (await _db.Baskets.Where(x => x.UserId == _targetId).Select(x => x.Name).ToListAsync(Ct)).Should().Equal("Good");
        (await _db.BasketItems.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Import_WithDuplicateProductRefInABasket_KeepsTheFirstQuantity()
    {
        var productRef = Guid.NewGuid();
        var bundle = Bundle(
            [BundleProduct(productRef, "P", "https://shop.test/p")],
            [new BackupBasket("Desk", null, [new BackupBasketItem(productRef, 2), new BackupBasketItem(productRef, 9)])]);

        await ImportAsync(bundle);

        _db.ChangeTracker.Clear();
        (await _db.BasketItems.SingleAsync(Ct)).Quantity.Should().Be(2);
    }

    [Fact]
    public async Task Import_OfAnOlderFileWithoutBasketsOrProductRefs_StillWorks()
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

        result.BasketsRestored.Should().Be(0);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
