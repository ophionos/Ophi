using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Baskets;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.Baskets;

public class BasketHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _db;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();

    public BasketHandlerTests()
    {
        (_db, _connection) = TestDbContextFactory.Create();
        _db.Users.Add(TestEntityFactory.User(_userId).Build());
        _db.Users.Add(TestEntityFactory.User(_otherUserId).Build());
        _db.SaveChanges();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Product> SeedProductAsync(Guid userId, string name = "Product", string? image = null)
    {
        var product = TestEntityFactory.Product(userId).Named(name).WithImage(image).Build();
        _db.Products.Add(product);
        await _db.SaveChangesAsync(Ct);
        return product;
    }

    private async Task<Basket> SeedBasketAsync(Guid userId, string name = "Setup", params (Product Product, int Quantity)[] items)
    {
        var basket = new Basket { Id = Guid.NewGuid(), UserId = userId, Name = name };
        foreach (var (product, quantity) in items) basket.AddItem(product.Id, quantity);
        _db.Baskets.Add(basket);
        await _db.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();
        return basket;
    }

    // --- CreateBasket ---

    [Fact]
    public async Task CreateBasket_WithName_PersistsEmptyBasketForTheUser()
    {
        var handler = new CreateBasket.Handler(_db, NullLogger<CreateBasket.Handler>.Instance);

        var result = await handler.Handle(new CreateBasket.Command("Desk", "Office gear") { UserId = _userId }, Ct);

        result.Name.Should().Be("Desk");
        result.Description.Should().Be("Office gear");
        result.Items.Should().BeEmpty();
        var saved = await _db.Baskets.AsNoTracking().SingleAsync(b => b.Id == result.Id, Ct);
        saved.UserId.Should().Be(_userId);
    }

    // --- GetBaskets ---

    [Fact]
    public async Task GetBaskets_ReturnsOwnBasketsWithItemCountNewestUpdatedFirst()
    {
        var p1 = await SeedProductAsync(_userId, "A");
        var p2 = await SeedProductAsync(_userId, "B");
        var older = await SeedBasketAsync(_userId, "Older", (p1, 1), (p2, 2));
        var newer = await SeedBasketAsync(_userId, "Newer");
        await SeedBasketAsync(_otherUserId, "Theirs");
        await _db.Baskets.Where(b => b.Id == older.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.UpdatedAt, DateTime.UtcNow.AddDays(-2)), Ct);
        await _db.Baskets.Where(b => b.Id == newer.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.UpdatedAt, DateTime.UtcNow.AddDays(-1)), Ct);

        var result = await new GetBaskets.Handler(_db, NullLogger<GetBaskets.Handler>.Instance)
            .Handle(new GetBaskets.Query(_userId), Ct);

        result.Items.Select(i => (i.Name, i.ItemCount)).Should().Equal(("Newer", 0), ("Older", 2));
    }

    // --- GetBasket ---

    [Fact]
    public async Task GetBasket_ReturnsItemsInTheOrderTheyWereAdded()
    {
        var first = await SeedProductAsync(_userId, "Zebra", "https://img.test/z.png");
        var second = await SeedProductAsync(_userId, "Apple");
        var basket = new Basket { Id = Guid.NewGuid(), UserId = _userId, Name = "Setup" };
        var i1 = basket.AddItem(first.Id, 3);
        var i2 = basket.AddItem(second.Id, 1);
        _db.Baskets.Add(basket);
        await _db.SaveChangesAsync(Ct);
        await _db.BasketItems.Where(i => i.ProductId == first.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.AddedAt, DateTime.UtcNow.AddHours(-2)), Ct);
        await _db.BasketItems.Where(i => i.ProductId == second.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.AddedAt, DateTime.UtcNow.AddHours(-1)), Ct);
        _db.ChangeTracker.Clear();
        _ = (i1, i2);

        var result = await new GetBasket.Handler(_db).Handle(new GetBasket.Query(basket.Id, _userId), Ct);

        result.Name.Should().Be("Setup");
        result.Items.Should().Equal(
            new BasketItemDto(first.Id, "Zebra", "https://img.test/z.png", 3),
            new BasketItemDto(second.Id, "Apple", null, 1));
    }

    [Fact]
    public async Task GetBasket_OfAnotherUser_ThrowsNotFound()
    {
        var basket = await SeedBasketAsync(_otherUserId);

        var act = () => new GetBasket.Handler(_db).Handle(new GetBasket.Query(basket.Id, _userId), Ct);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // --- UpdateBasket ---

    [Fact]
    public async Task UpdateBasket_RenamesAndKeepsItems()
    {
        var product = await SeedProductAsync(_userId);
        var basket = await SeedBasketAsync(_userId, "Old", (product, 2));
        var handler = new UpdateBasket.Handler(_db, NullLogger<UpdateBasket.Handler>.Instance);

        var result = await handler.Handle(new UpdateBasket.Command(basket.Id, "New", "Desc") { UserId = _userId }, Ct);

        result.Name.Should().Be("New");
        result.Description.Should().Be("Desc");
        result.Items.Should().ContainSingle().Which.Quantity.Should().Be(2);
    }

    [Fact]
    public async Task UpdateBasket_OfAnotherUser_ThrowsNotFoundAndChangesNothing()
    {
        var basket = await SeedBasketAsync(_otherUserId, "Theirs");
        var handler = new UpdateBasket.Handler(_db, NullLogger<UpdateBasket.Handler>.Instance);

        var act = () => handler.Handle(new UpdateBasket.Command(basket.Id, "Mine now") { UserId = _userId }, Ct);

        await act.Should().ThrowAsync<NotFoundException>();
        (await _db.Baskets.AsNoTracking().SingleAsync(b => b.Id == basket.Id, Ct)).Name.Should().Be("Theirs");
    }

    // --- DeleteBasket ---

    [Fact]
    public async Task DeleteBasket_RemovesTheBasketAndItsItemsButNotTheProducts()
    {
        var product = await SeedProductAsync(_userId);
        var basket = await SeedBasketAsync(_userId, "Setup", (product, 1));

        await new DeleteBasket.Handler(_db, NullLogger<DeleteBasket.Handler>.Instance)
            .Handle(new DeleteBasket.Command(basket.Id, _userId), Ct);

        (await _db.Baskets.AnyAsync(b => b.Id == basket.Id, Ct)).Should().BeFalse();
        (await _db.BasketItems.AnyAsync(i => i.BasketId == basket.Id, Ct)).Should().BeFalse();
        (await _db.Products.AnyAsync(p => p.Id == product.Id, Ct)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteBasket_OfAnotherUser_ThrowsNotFoundAndKeepsIt()
    {
        var basket = await SeedBasketAsync(_otherUserId);

        var act = () => new DeleteBasket.Handler(_db, NullLogger<DeleteBasket.Handler>.Instance)
            .Handle(new DeleteBasket.Command(basket.Id, _userId), Ct);

        await act.Should().ThrowAsync<NotFoundException>();
        (await _db.Baskets.AnyAsync(b => b.Id == basket.Id, Ct)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteProduct_RemovesItsBasketItems()
    {
        var kept = await SeedProductAsync(_userId, "Kept");
        var doomed = await SeedProductAsync(_userId, "Doomed");
        var basket = await SeedBasketAsync(_userId, "Setup", (kept, 1), (doomed, 1));

        _db.Products.Remove(await _db.Products.SingleAsync(p => p.Id == doomed.Id, Ct));
        await _db.SaveChangesAsync(Ct);

        var remaining = await _db.BasketItems.Where(i => i.BasketId == basket.Id).Select(i => i.ProductId).ToListAsync(Ct);
        remaining.Should().Equal(kept.Id);
    }

    // --- AddBasketItems ---

    [Fact]
    public async Task AddBasketItems_AddsProductsWithTheGivenQuantity()
    {
        var p1 = await SeedProductAsync(_userId, "A");
        var p2 = await SeedProductAsync(_userId, "B");
        var basket = await SeedBasketAsync(_userId);
        var handler = new AddBasketItems.Handler(_db, NullLogger<AddBasketItems.Handler>.Instance);

        var result = await handler.Handle(new AddBasketItems.Command(basket.Id, [p1.Id, p2.Id], 3) { UserId = _userId }, Ct);

        result.Items.Select(i => (i.ProductId, i.Quantity)).Should().BeEquivalentTo([(p1.Id, 3), (p2.Id, 3)]);
    }

    [Fact]
    public async Task AddBasketItems_WithoutQuantity_DefaultsToOne()
    {
        var product = await SeedProductAsync(_userId);
        var basket = await SeedBasketAsync(_userId);

        var result = await new AddBasketItems.Handler(_db, NullLogger<AddBasketItems.Handler>.Instance)
            .Handle(new AddBasketItems.Command(basket.Id, [product.Id]) { UserId = _userId }, Ct);

        result.Items.Should().ContainSingle().Which.Quantity.Should().Be(1);
    }

    [Fact]
    public async Task AddBasketItems_WithProductAlreadyInBasket_ChangesNothing()
    {
        var existing = await SeedProductAsync(_userId, "Existing");
        var added = await SeedProductAsync(_userId, "Added");
        var basket = await SeedBasketAsync(_userId, "Setup", (existing, 5));

        var result = await new AddBasketItems.Handler(_db, NullLogger<AddBasketItems.Handler>.Instance)
            .Handle(new AddBasketItems.Command(basket.Id, [existing.Id, existing.Id, added.Id], 2) { UserId = _userId }, Ct);

        result.Items.Should().HaveCount(2);
        result.Items.Single(i => i.ProductId == existing.Id).Quantity.Should().Be(5);
        result.Items.Single(i => i.ProductId == added.Id).Quantity.Should().Be(2);
    }

    [Fact]
    public async Task AddBasketItems_ToAnotherUsersBasket_ThrowsNotFound()
    {
        var product = await SeedProductAsync(_userId);
        var basket = await SeedBasketAsync(_otherUserId);

        var act = () => new AddBasketItems.Handler(_db, NullLogger<AddBasketItems.Handler>.Instance)
            .Handle(new AddBasketItems.Command(basket.Id, [product.Id]) { UserId = _userId }, Ct);

        await act.Should().ThrowAsync<NotFoundException>();
        (await _db.BasketItems.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task AddBasketItems_WithAnotherUsersProduct_ThrowsNotFoundAndAddsNothing()
    {
        var mine = await SeedProductAsync(_userId);
        var theirs = await SeedProductAsync(_otherUserId);
        var basket = await SeedBasketAsync(_userId);

        var act = () => new AddBasketItems.Handler(_db, NullLogger<AddBasketItems.Handler>.Instance)
            .Handle(new AddBasketItems.Command(basket.Id, [mine.Id, theirs.Id]) { UserId = _userId }, Ct);

        await act.Should().ThrowAsync<NotFoundException>();
        _db.ChangeTracker.Clear();
        (await _db.BasketItems.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task AddBasketItems_WithUnknownProduct_ThrowsNotFound()
    {
        var basket = await SeedBasketAsync(_userId);

        var act = () => new AddBasketItems.Handler(_db, NullLogger<AddBasketItems.Handler>.Instance)
            .Handle(new AddBasketItems.Command(basket.Id, [Guid.NewGuid()]) { UserId = _userId }, Ct);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // --- SetBasketItemQuantity ---

    [Fact]
    public async Task SetBasketItemQuantity_ChangesTheQuantity()
    {
        var product = await SeedProductAsync(_userId);
        var basket = await SeedBasketAsync(_userId, "Setup", (product, 1));

        var result = await new SetBasketItemQuantity.Handler(_db, NullLogger<SetBasketItemQuantity.Handler>.Instance)
            .Handle(new SetBasketItemQuantity.Command(basket.Id, product.Id, 7) { UserId = _userId }, Ct);

        result.Items.Should().ContainSingle().Which.Quantity.Should().Be(7);
        _db.ChangeTracker.Clear();
        (await _db.BasketItems.SingleAsync(Ct)).Quantity.Should().Be(7);
    }

    [Fact]
    public async Task SetBasketItemQuantity_ForProductNotInBasket_ThrowsNotFound()
    {
        var product = await SeedProductAsync(_userId);
        var basket = await SeedBasketAsync(_userId);

        var act = () => new SetBasketItemQuantity.Handler(_db, NullLogger<SetBasketItemQuantity.Handler>.Instance)
            .Handle(new SetBasketItemQuantity.Command(basket.Id, product.Id, 2) { UserId = _userId }, Ct);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SetBasketItemQuantity_InAnotherUsersBasket_ThrowsNotFoundAndChangesNothing()
    {
        var theirs = await SeedProductAsync(_otherUserId);
        var basket = await SeedBasketAsync(_otherUserId, "Theirs", (theirs, 1));

        var act = () => new SetBasketItemQuantity.Handler(_db, NullLogger<SetBasketItemQuantity.Handler>.Instance)
            .Handle(new SetBasketItemQuantity.Command(basket.Id, theirs.Id, 9) { UserId = _userId }, Ct);

        await act.Should().ThrowAsync<NotFoundException>();
        _db.ChangeTracker.Clear();
        (await _db.BasketItems.SingleAsync(Ct)).Quantity.Should().Be(1);
    }

    // --- RemoveBasketItem ---

    [Fact]
    public async Task RemoveBasketItem_RemovesOnlyThatItem()
    {
        var keep = await SeedProductAsync(_userId, "Keep");
        var drop = await SeedProductAsync(_userId, "Drop");
        var basket = await SeedBasketAsync(_userId, "Setup", (keep, 1), (drop, 1));

        await new RemoveBasketItem.Handler(_db, NullLogger<RemoveBasketItem.Handler>.Instance)
            .Handle(new RemoveBasketItem.Command(basket.Id, drop.Id, _userId), Ct);

        _db.ChangeTracker.Clear();
        (await _db.BasketItems.Select(i => i.ProductId).ToListAsync(Ct)).Should().Equal(keep.Id);
        (await _db.Products.AnyAsync(p => p.Id == drop.Id, Ct)).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveBasketItem_ForProductNotInBasket_ThrowsNotFound()
    {
        var product = await SeedProductAsync(_userId);
        var basket = await SeedBasketAsync(_userId);

        var act = () => new RemoveBasketItem.Handler(_db, NullLogger<RemoveBasketItem.Handler>.Instance)
            .Handle(new RemoveBasketItem.Command(basket.Id, product.Id, _userId), Ct);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task RemoveBasketItem_InAnotherUsersBasket_ThrowsNotFoundAndKeepsTheItem()
    {
        var theirs = await SeedProductAsync(_otherUserId);
        var basket = await SeedBasketAsync(_otherUserId, "Theirs", (theirs, 1));

        var act = () => new RemoveBasketItem.Handler(_db, NullLogger<RemoveBasketItem.Handler>.Instance)
            .Handle(new RemoveBasketItem.Command(basket.Id, theirs.Id, _userId), Ct);

        await act.Should().ThrowAsync<NotFoundException>();
        (await _db.BasketItems.CountAsync(Ct)).Should().Be(1);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
