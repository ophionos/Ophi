using FluentValidation.TestHelper;
using Ophi.Api.Features.Comparisons;

namespace Ophi.Api.Tests.Unit.Comparisons;

public class AddProductsToGroupValidatorTests
{
    private readonly AddProductsToGroup.Validator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_ShouldNotHaveErrors()
    {
        var command = new AddProductsToGroup.Command(Guid.NewGuid(), [Guid.NewGuid(), Guid.NewGuid()])
        { UserId = Guid.NewGuid() };

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyGroupId_ShouldHaveError()
    {
        var command = new AddProductsToGroup.Command(Guid.Empty, [Guid.NewGuid()])
        { UserId = Guid.NewGuid() };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.GroupId);
    }

    [Fact]
    public void Validate_WithEmptyProductIds_ShouldHaveError()
    {
        var command = new AddProductsToGroup.Command(Guid.NewGuid(), [])
        { UserId = Guid.NewGuid() };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ProductIds);
    }

    [Fact]
    public void Validate_WithTooManyProductIds_ShouldHaveError()
    {
        var ids = Enumerable.Range(0, AddProductsToGroup.MaxProductsPerRequest + 1)
            .Select(_ => Guid.NewGuid())
            .ToList();
        var command = new AddProductsToGroup.Command(Guid.NewGuid(), ids)
        { UserId = Guid.NewGuid() };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ProductIds);
    }

    [Fact]
    public void Validate_WithEmptyProductIdInList_ShouldHaveError()
    {
        var command = new AddProductsToGroup.Command(Guid.NewGuid(), [Guid.NewGuid(), Guid.Empty])
        { UserId = Guid.NewGuid() };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("ProductIds[1]");
    }
}
