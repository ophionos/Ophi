using FluentAssertions;
using FluentValidation.TestHelper;
using Ophi.Api.Features.Stores;

namespace Ophi.Api.Tests.Unit.Stores;

public class CreateStoreValidatorTests
{
    private readonly CreateStore.Validator _validator = new();

    private static CreateStore.Command ValidCommand(string? currencyOverride = null) =>
        new(
            "test-store",
            "Test Store",
            ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null),
            "en-US",
            false,
            currencyOverride
        )
        { UserId = Guid.NewGuid() };

    [Fact]
    public void Validate_WithValidCurrencyOverride_PassesValidation()
    {
        var result = _validator.TestValidate(ValidCommand("EUR"));
        result.ShouldNotHaveValidationErrorFor(x => x.CurrencyOverride);
    }

    [Fact]
    public void Validate_WithNullCurrencyOverride_PassesValidation()
    {
        var result = _validator.TestValidate(ValidCommand());
        result.ShouldNotHaveValidationErrorFor(x => x.CurrencyOverride);
    }

    [Theory]
    [InlineData("eur")]
    [InlineData("Eur")]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("12")]
    [InlineData("")]
    public void Validate_WithInvalidCurrencyOverride_FailsValidation(string currency)
    {
        var result = _validator.TestValidate(ValidCommand(currency));
        result.ShouldHaveValidationErrorFor(x => x.CurrencyOverride);
    }

    [Fact]
    public void Validate_WithValidJsonPaths_PassesValidation()
    {
        var command = new CreateStore.Command(
            "test-store", "Test", ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null,
                ["$.offers.price"], ["$.name"], ["$.image"]),
            "en-US", false, null
        ) { UserId = Guid.NewGuid() };

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithNullJsonPaths_PassesValidation()
    {
        var command = ValidCommand();
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithInvalidJsonPath_FailsValidation()
    {
        var command = new CreateStore.Command(
            "test-store", "Test", ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null,
                ["[[[invalid"]),
            "en-US", false, null
        ) { UserId = Guid.NewGuid() };

        var result = _validator.TestValidate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Invalid JSONPath expression");
    }

    [Fact]
    public void Validate_WithWhitespaceJsonPath_FailsValidation()
    {
        var command = new CreateStore.Command(
            "test-store", "Test", ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null,
                PriceJsonPaths: ["  "]),
            "en-US", false, null
        ) { UserId = Guid.NewGuid() };

        var result = _validator.TestValidate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Invalid JSONPath expression");
    }
}
