using FluentAssertions;
using Ophi.Domain.Entities;

namespace Ophi.Domain.Tests;

public class BasketTests
{
    private static Basket NewBasket() => new() { Id = Guid.NewGuid(), Name = "Setup", UserId = Guid.NewGuid() };

    [Fact]
    public void AddItem_WithNewProduct_AddsItemWithQuantity()
    {
        var basket = NewBasket();
        var productId = Guid.NewGuid();

        var item = basket.AddItem(productId, 3);

        item.ProductId.Should().Be(productId);
        item.BasketId.Should().Be(basket.Id);
        item.Quantity.Should().Be(3);
        item.AddedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        basket.Items.Should().ContainSingle().Which.Should().BeSameAs(item);
    }

    [Fact]
    public void AddItem_WithoutQuantity_DefaultsToOne()
    {
        var item = NewBasket().AddItem(Guid.NewGuid());

        item.Quantity.Should().Be(1);
    }

    [Fact]
    public void AddItem_WithExistingProduct_ReturnsExistingItemAndKeepsItsQuantity()
    {
        var basket = NewBasket();
        var productId = Guid.NewGuid();
        var first = basket.AddItem(productId, 4);

        var second = basket.AddItem(productId, 9);

        second.Should().BeSameAs(first);
        second.Quantity.Should().Be(4);
        basket.Items.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddItem_WithQuantityBelowOne_Throws(int quantity)
    {
        var act = () => NewBasket().AddItem(Guid.NewGuid(), quantity);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetQuantity_WithExistingProduct_ChangesQuantity()
    {
        var basket = NewBasket();
        var productId = Guid.NewGuid();
        basket.AddItem(productId, 1);

        basket.SetQuantity(productId, 5);

        basket.Items.Single().Quantity.Should().Be(5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void SetQuantity_WithQuantityBelowOne_Throws(int quantity)
    {
        var basket = NewBasket();
        var productId = Guid.NewGuid();
        basket.AddItem(productId, 2);

        var act = () => basket.SetQuantity(productId, quantity);

        act.Should().Throw<ArgumentOutOfRangeException>();
        basket.Items.Single().Quantity.Should().Be(2);
    }

    [Fact]
    public void SetQuantity_WithProductNotInBasket_ThrowsInvalidOperation()
    {
        var act = () => NewBasket().SetQuantity(Guid.NewGuid(), 2);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RemoveItem_WithExistingProduct_RemovesItAndReturnsTrue()
    {
        var basket = NewBasket();
        var productId = Guid.NewGuid();
        basket.AddItem(productId);

        var removed = basket.RemoveItem(productId);

        removed.Should().BeTrue();
        basket.Items.Should().BeEmpty();
    }

    [Fact]
    public void RemoveItem_WithMissingProduct_ReturnsFalse()
    {
        var basket = NewBasket();
        basket.AddItem(Guid.NewGuid());

        var removed = basket.RemoveItem(Guid.NewGuid());

        removed.Should().BeFalse();
        basket.Items.Should().HaveCount(1);
    }

    [Fact]
    public void Rename_ChangesNameAndDescription()
    {
        var basket = NewBasket();

        basket.Rename("Office", "Desk gear");

        basket.Name.Should().Be("Office");
        basket.Description.Should().Be("Desk gear");
    }

    [Fact]
    public void Rename_WithNullDescription_ClearsIt()
    {
        var basket = new Basket { Id = Guid.NewGuid(), Name = "A", Description = "old" };

        basket.Rename("A", null);

        basket.Description.Should().BeNull();
    }
}
