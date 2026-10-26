using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Entities;
using Ophi.TestHelpers;

namespace Ophi.Postgres.Tests;

/// <summary>Basket constraints and cascades on real Postgres (the migration history, not the EF model).</summary>
[Collection("Postgres")]
public class BasketPersistenceTests(PostgresFixture fixture)
{
    private async Task<(Guid UserId, Guid BasketId, Guid ProductId)> SeedAsync()
    {
        var userId = Guid.NewGuid();
        var product = TestEntityFactory.Product(userId).Named($"Product {Guid.NewGuid():N}").Build();
        var basket = new Basket { Id = Guid.NewGuid(), UserId = userId, Name = "Setup" };
        basket.AddItem(product.Id, 2);

        await using var ctx = fixture.CreateContext();
        ctx.Users.Add(TestEntityFactory.User(userId).Build());
        ctx.Products.Add(product);
        ctx.Baskets.Add(basket);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (userId, basket.Id, product.Id);
    }

    [Fact]
    public async Task SaveChanges_WithDuplicateBasketAndProduct_ThrowsUniqueViolation()
    {
        var (_, basketId, productId) = await SeedAsync();

        await using var ctx = fixture.CreateContext();
        ctx.BasketItems.Add(new BasketItem { BasketId = basketId, ProductId = productId, Quantity = 1, AddedAt = DateTime.UtcNow });
        var act = () => ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DeletingProduct_DeletesItsBasketItems_AndKeepsTheBasket()
    {
        var (_, basketId, productId) = await SeedAsync();

        await using (var ctx = fixture.CreateContext())
        {
            var product = await ctx.Products.SingleAsync(p => p.Id == productId, TestContext.Current.CancellationToken);
            ctx.Products.Remove(product);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = fixture.CreateContext();
        (await verify.BasketItems.CountAsync(i => i.BasketId == basketId, TestContext.Current.CancellationToken)).Should().Be(0);
        (await verify.Baskets.AnyAsync(b => b.Id == basketId, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task DeletingBasket_DeletesItsItems_AndKeepsTheProduct()
    {
        var (_, basketId, productId) = await SeedAsync();

        await using (var ctx = fixture.CreateContext())
        {
            var basket = await ctx.Baskets.SingleAsync(b => b.Id == basketId, TestContext.Current.CancellationToken);
            ctx.Baskets.Remove(basket);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = fixture.CreateContext();
        (await verify.BasketItems.CountAsync(i => i.BasketId == basketId, TestContext.Current.CancellationToken)).Should().Be(0);
        (await verify.Products.AnyAsync(p => p.Id == productId, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task DeletingUser_DeletesBasketsAndItems()
    {
        var (userId, basketId, _) = await SeedAsync();

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Users.Remove(await ctx.Users.SingleAsync(u => u.Id == userId, TestContext.Current.CancellationToken));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = fixture.CreateContext();
        (await verify.Baskets.AnyAsync(b => b.Id == basketId, TestContext.Current.CancellationToken)).Should().BeFalse();
        (await verify.BasketItems.AnyAsync(i => i.BasketId == basketId, TestContext.Current.CancellationToken)).Should().BeFalse();
    }
}
