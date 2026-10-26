using FluentValidation.TestHelper;
using Ophi.Api.Features.Baskets;

namespace Ophi.Api.Tests.Unit.Baskets;

public class BasketValidatorTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();

    [Fact]
    public void CreateBasket_WithValidCommand_HasNoErrors()
    {
        new CreateBasket.Validator().TestValidate(new CreateBasket.Command("Desk", "Gear"))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateBasket_WithBlankName_HasError(string name)
    {
        new CreateBasket.Validator().TestValidate(new CreateBasket.Command(name))
            .ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void CreateBasket_WithNameOver100Chars_HasError()
    {
        new CreateBasket.Validator().TestValidate(new CreateBasket.Command(new string('a', 101)))
            .ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void CreateBasket_WithDescriptionOver500Chars_HasError()
    {
        new CreateBasket.Validator().TestValidate(new CreateBasket.Command("Desk", new string('a', 501)))
            .ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void UpdateBasket_WithBlankName_HasError()
    {
        new UpdateBasket.Validator().TestValidate(new UpdateBasket.Command(Id, ""))
            .ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void AddBasketItems_WithValidCommand_HasNoErrors()
    {
        new AddBasketItems.Validator().TestValidate(new AddBasketItems.Command(Id, [ProductId], 999))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void AddBasketItems_WithoutQuantity_HasNoErrors()
    {
        new AddBasketItems.Validator().TestValidate(new AddBasketItems.Command(Id, [ProductId]))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void AddBasketItems_WithNoProducts_HasError()
    {
        new AddBasketItems.Validator().TestValidate(new AddBasketItems.Command(Id, []))
            .ShouldHaveValidationErrorFor(x => x.ProductIds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000)]
    public void AddBasketItems_WithQuantityOutsideRange_HasError(int quantity)
    {
        new AddBasketItems.Validator().TestValidate(new AddBasketItems.Command(Id, [ProductId], quantity))
            .ShouldHaveValidationErrorFor(x => x.Quantity);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(999)]
    public void SetBasketItemQuantity_WithQuantityInRange_HasNoErrors(int quantity)
    {
        new SetBasketItemQuantity.Validator().TestValidate(new SetBasketItemQuantity.Command(Id, ProductId, quantity))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public void SetBasketItemQuantity_WithQuantityOutsideRange_HasError(int quantity)
    {
        new SetBasketItemQuantity.Validator().TestValidate(new SetBasketItemQuantity.Command(Id, ProductId, quantity))
            .ShouldHaveValidationErrorFor(x => x.Quantity)
            .WithErrorMessage("Quantity must be between 1 and 999");
    }
}
