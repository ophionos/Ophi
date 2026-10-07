using FluentAssertions;
using FluentValidation.TestHelper;
using Ophi.Api.Features.Products;

namespace Ophi.Api.Tests.Unit.Validators;

public class CreateProductValidatorTests
{
    private readonly CreateProduct.Validator _validator = new();

    [Fact]
    public void Validate_WithValidName_ShouldNotHaveErrors()
    {
        var command = new CreateProduct.Command("My Product");
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyName_ShouldHaveError()
    {
        var command = new CreateProduct.Command("");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_WithNameExceeding500Characters_ShouldHaveError()
    {
        var command = new CreateProduct.Command(new string('a', 501));
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_WithValidImageUrl_ShouldNotHaveErrors()
    {
        var command = new CreateProduct.Command("Product", "https://example.com/image.jpg");
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithInvalidImageUrl_ShouldHaveError()
    {
        var command = new CreateProduct.Command("Product", "not-a-url");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ImageUrl)
            .WithErrorMessage("Must be a valid HTTP or HTTPS URL");
    }

    [Fact]
    public void Validate_WithFtpImageUrl_ShouldHaveError()
    {
        var command = new CreateProduct.Command("Product", "ftp://example.com/image.jpg");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ImageUrl)
            .WithErrorMessage("Must be a valid HTTP or HTTPS URL");
    }

    [Fact]
    public void Validate_WithNullImageUrl_ShouldNotHaveErrors()
    {
        var command = new CreateProduct.Command("Product");
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithImageUrlExceeding2048Characters_ShouldHaveError()
    {
        var longUrl = "https://example.com/" + new string('a', 2030);
        var command = new CreateProduct.Command("Product", longUrl);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ImageUrl);
    }

    [Fact]
    public void Validate_WithValidCurrency_ShouldNotHaveErrors()
    {
        var command = new CreateProduct.Command("Product", Currency: "EUR");
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithInvalidCurrency_ShouldHaveError()
    {
        var command = new CreateProduct.Command("Product", Currency: "usd");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Currency)
            .WithErrorMessage("Currency must be a 3-letter uppercase code (e.g., USD, EUR)");
    }

    [Fact]
    public void Validate_WithTooShortCurrency_ShouldHaveError()
    {
        var command = new CreateProduct.Command("Product", Currency: "US");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Currency);
    }

    [Fact]
    public void Validate_WithNullCurrency_ShouldNotHaveErrors()
    {
        var command = new CreateProduct.Command("Product", Currency: null);
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithAllValidFields_ShouldNotHaveErrors()
    {
        var command = new CreateProduct.Command("Product", "https://example.com/img.jpg", "GBP");
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithValidCustomFields_ShouldNotHaveErrors()
    {
        var command = new CreateProduct.Command("Product", CustomFields:
            [new CustomFieldDto("Color", "Red"), new CustomFieldDto("Size", "Large")]);
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyCustomFieldName_ShouldHaveError()
    {
        var command = new CreateProduct.Command("Product", CustomFields:
            [new CustomFieldDto("", "Red")]);
        var result = _validator.TestValidate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Custom field name is required");
    }

    [Fact]
    public void Validate_WithCustomFieldNameTooLong_ShouldHaveError()
    {
        var command = new CreateProduct.Command("Product", CustomFields:
            [new CustomFieldDto(new string('a', 101), "Red")]);
        var result = _validator.TestValidate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Custom field name must not exceed 100 characters");
    }

    [Fact]
    public void Validate_WithCustomFieldValueTooLong_ShouldHaveError()
    {
        var command = new CreateProduct.Command("Product", CustomFields:
            [new CustomFieldDto("Color", new string('a', 1001))]);
        var result = _validator.TestValidate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Custom field value must not exceed 1000 characters");
    }

    [Fact]
    public void Validate_WithTooManyCustomFields_ShouldHaveError()
    {
        var fields = Enumerable.Range(1, 51)
            .Select(i => new CustomFieldDto($"Field{i}", $"Value{i}"))
            .ToList();
        var command = new CreateProduct.Command("Product", CustomFields: fields);
        var result = _validator.TestValidate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Cannot have more than 50 custom fields");
    }

    [Fact]
    public void Validate_WithDuplicateCustomFieldNames_ShouldHaveError()
    {
        var command = new CreateProduct.Command("Product", CustomFields:
            [new CustomFieldDto("Color", "Red"), new CustomFieldDto("color", "Blue")]);
        var result = _validator.TestValidate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Custom field names must be unique");
    }
}
