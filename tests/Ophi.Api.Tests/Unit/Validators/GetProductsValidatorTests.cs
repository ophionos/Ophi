using FluentValidation.TestHelper;
using Ophi.Api.Features.Products;

namespace Ophi.Api.Tests.Unit.Validators;

public class GetProductsValidatorTests
{
    private readonly GetProducts.Validator _validator = new();

    [Fact]
    public void Validate_WithNullSearch_ShouldNotHaveErrors()
    {
        var query = new GetProducts.Query(Guid.NewGuid(), Search: null);

        var result = _validator.TestValidate(query);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithShortSearch_ShouldNotHaveErrors()
    {
        var query = new GetProducts.Query(Guid.NewGuid(), Search: "laptop");

        var result = _validator.TestValidate(query);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithExactly100CharSearch_ShouldNotHaveErrors()
    {
        var query = new GetProducts.Query(Guid.NewGuid(), Search: new string('a', 100));

        var result = _validator.TestValidate(query);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_With101CharSearch_ShouldHaveError()
    {
        var query = new GetProducts.Query(Guid.NewGuid(), Search: new string('a', 101));

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.Search)
            .WithErrorMessage("Search term must not exceed 100 characters");
    }

    [Fact]
    public void Validate_WithLargeSearch_ShouldHaveError()
    {
        var query = new GetProducts.Query(Guid.NewGuid(), Search: new string('%', 10_000));

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.Search);
    }
}
