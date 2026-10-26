using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Baskets;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Baskets;

/// <summary>
/// The basket alert slices (#151). Basket alerts have their own active limit: MaxAlertsPerUser counted
/// over basket alerts only, enforced on create and resume. Product alerts do not count toward it.
/// </summary>
public class BasketAlertHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _db;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();

    public BasketAlertHandlerTests()
    {
        (_db, _connection) = TestDbContextFactory.Create();
        _db.Users.Add(TestEntityFactory.User(_userId).Build());
        _db.Users.Add(TestEntityFactory.User(_otherUserId).Build());
        _db.SaveChanges();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IOptions<AlertSettings> Settings(int max = 100) => Options.Create(new AlertSettings { MaxAlertsPerUser = max });

    private Product AddProduct(string name, params (string Host, decimal Price, string Currency)[] urls)
    {
        var product = new Product { Id = Guid.NewGuid(), UserId = _userId, Name = name, Currency = "EUR" };
        _db.Products.Add(product);
        foreach (var (host, price, currency) in urls)
        {
            _db.ProductUrls.Add(new ProductUrl
            {
                Id = Guid.NewGuid(), ProductId = product.Id, Url = $"https://www.{host}/{Guid.NewGuid():N}",
                CurrentPrice = price, Currency = currency, Status = ProductUrlStatus.Active
            });
        }
        return product;
    }

    private async Task<Guid> BasketWith(Guid userId, params Product[] products)
    {
        var basket = new Basket { Id = Guid.NewGuid(), UserId = userId, Name = "Setup" };
        foreach (var product in products) basket.AddItem(product.Id, 1);
        _db.Baskets.Add(basket);
        await _db.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();
        return basket.Id;
    }

    private Task<BasketAlertDto> Create(Guid basketId, decimal target, string? host = null, int max = 100) =>
        new CreateBasketAlert.Handler(_db, Settings(max), NullLogger<CreateBasketAlert.Handler>.Instance)
            .Handle(new CreateBasketAlert.Command(basketId, target, host) { UserId = _userId }, Ct);

    private Task<BasketAlertDto> SetActive(Guid basketId, Guid alertId, bool active, int max = 100) =>
        new SetBasketAlertActive.Handler(_db, Settings(max), NullLogger<SetBasketAlertActive.Handler>.Instance)
            .Handle(new SetBasketAlertActive.Command(basketId, alertId, active) { UserId = _userId }, Ct);

    [Fact]
    public async Task Create_ForTheBestStore_StampsTheWatchedCurrencyAndReportsTheWatchedTotal()
    {
        var basketId = await BasketWith(_userId, AddProduct("Tent", ("one.example", 30m, "EUR"), ("two.example", 25m, "EUR")));

        var result = await Create(basketId, 20m);

        result.Should().BeEquivalentTo(new
        {
            BasketId = basketId, Host = (string?)null, TargetTotal = 20m, Currency = "EUR", Active = true,
            WatchedHost = "two.example", WatchedTotal = 25m, WatchedCurrency = "EUR", HasCurrencyMismatch = false
        });
        _db.BasketAlerts.Should().ContainSingle(a => a.BasketId == basketId && a.UserId == _userId);
    }

    [Fact]
    public async Task Create_ForANamedStore_StoresTheNormalizedHost()
    {
        var basketId = await BasketWith(_userId, AddProduct("Tent", ("one.example", 30m, "EUR")));

        var result = await Create(basketId, 20m, "WWW.One.Example");

        result.Host.Should().Be("one.example");
        result.WatchedTotal.Should().Be(30m);
    }

    [Theory]
    [InlineData("  one.example  ")]
    [InlineData("https://www.one.example/tent?ref=1")]
    public async Task Create_WithAPaddedHostOrAUrl_StoresTheSiteHost(string host)
    {
        var basketId = await BasketWith(_userId, AddProduct("Tent", ("one.example", 30m, "EUR")));

        var result = await Create(basketId, 20m, host);

        result.Host.Should().Be("one.example");
        result.WatchedTotal.Should().Be(30m);
    }

    [Fact]
    public async Task Create_WhenTheStoreHasNoTotal_Fails422AndStoresNothing()
    {
        // two.example lacks the stove, so it has no total to watch.
        var basketId = await BasketWith(_userId,
            AddProduct("Tent", ("one.example", 30m, "EUR"), ("two.example", 25m, "EUR")),
            AddProduct("Stove", ("one.example", 10m, "EUR")));

        var act = () => Create(basketId, 20m, "two.example");

        (await act.Should().ThrowAsync<ApiException>()).Which.Should().BeEquivalentTo(new { StatusCode = 422, ErrorCode = "NoWatchedTotal" });
        _db.BasketAlerts.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_InAnEmptyBasket_Fails422()
    {
        var basketId = await BasketWith(_userId);

        var act = () => Create(basketId, 20m);

        (await act.Should().ThrowAsync<ApiException>()).Which.ErrorCode.Should().Be("NoWatchedTotal");
    }

    [Fact]
    public async Task Create_OnAnotherUsersBasket_IsNotFound()
    {
        var basketId = await BasketWith(_otherUserId);

        var act = () => Create(basketId, 20m);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Create_AtTheLimit_Fails422_AndProductAlertsDoNotCount()
    {
        var tent = AddProduct("Tent", ("one.example", 30m, "EUR"));
        _db.Alerts.Add(TestEntityFactory.CreateAlert(tent.Id, _userId, 10m));
        var basketId = await BasketWith(_userId, tent);

        await Create(basketId, 20m, max: 1); // the product alert does not use up the one place
        var act = () => Create(basketId, 25m, max: 1);

        (await act.Should().ThrowAsync<ApiException>()).Which.Should().BeEquivalentTo(new { StatusCode = 422, ErrorCode = "MaxAlertsReached" });
    }

    [Fact]
    public async Task SetActive_ResumeAtTheLimit_Fails422()
    {
        var basketId = await BasketWith(_userId, AddProduct("Tent", ("one.example", 30m, "EUR")));
        var first = await Create(basketId, 20m, max: 1);
        await SetActive(basketId, first.Id, false, max: 1);
        await Create(basketId, 25m, max: 1);

        var act = () => SetActive(basketId, first.Id, true, max: 1);

        (await act.Should().ThrowAsync<ApiException>()).Which.ErrorCode.Should().Be("MaxAlertsReached");
    }

    [Fact]
    public async Task SetActive_Pause_KeepsTheAlertAndStopsIt()
    {
        var basketId = await BasketWith(_userId, AddProduct("Tent", ("one.example", 30m, "EUR")));
        var created = await Create(basketId, 20m);

        var result = await SetActive(basketId, created.Id, false);

        result.Active.Should().BeFalse();
        _db.ChangeTracker.Clear();
        _db.BasketAlerts.Single().IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetBasketAlerts_ReportsAMismatchWhenTheWatchedTotalIsInAnotherCurrency()
    {
        var tent = AddProduct("Tent", ("one.example", 30m, "EUR"));
        var basketId = await BasketWith(_userId, tent);
        _db.BasketAlerts.Add(new BasketAlert { Id = Guid.NewGuid(), BasketId = basketId, UserId = _userId, TargetTotal = 20m, Currency = "USD" });
        await _db.SaveChangesAsync(Ct);

        var result = await new GetBasketAlerts.Handler(_db).Handle(new GetBasketAlerts.Query(basketId, _userId), Ct);

        result.Items.Should().ContainSingle().Which.HasCurrencyMismatch.Should().BeTrue();
    }

    [Fact]
    public async Task GetBasketAlerts_OnAnotherUsersBasket_IsNotFound()
    {
        var basketId = await BasketWith(_otherUserId);

        var act = () => new GetBasketAlerts.Handler(_db).Handle(new GetBasketAlerts.Query(basketId, _userId), Ct);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Delete_RemovesOnlyTheUsersAlert()
    {
        var basketId = await BasketWith(_userId, AddProduct("Tent", ("one.example", 30m, "EUR")));
        var created = await Create(basketId, 20m);
        var handler = new DeleteBasketAlert.Handler(_db, NullLogger<DeleteBasketAlert.Handler>.Instance);

        var otherUser = () => handler.Handle(new DeleteBasketAlert.Command(basketId, created.Id, _otherUserId), Ct);
        await otherUser.Should().ThrowAsync<NotFoundException>();

        await handler.Handle(new DeleteBasketAlert.Command(basketId, created.Id, _userId), Ct);
        _db.BasketAlerts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_NonPositiveTarget_HasError(decimal target) =>
        new CreateBasketAlert.Validator().TestValidate(new CreateBasketAlert.Command(Guid.NewGuid(), target, null))
            .ShouldHaveValidationErrorFor(x => x.TargetTotal);

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
