using FluentAssertions;
using FluentValidation.TestHelper;
using Ophi.Api.Features.Stores;

namespace Ophi.Api.Tests.Unit.Stores;

public class UpdateStoreValidatorTests
{
    private readonly UpdateStore.Validator _validator = new();

    private static UpdateStore.Command ValidCommand(string? currencyOverride = null) =>
        new(
            Guid.NewGuid(),
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
        var result = _validator.TestValidate(ValidCommand("USD"));
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
    [InlineData("")]
    public void Validate_WithInvalidCurrencyOverride_FailsValidation(string currency)
    {
        var result = _validator.TestValidate(ValidCommand(currency));
        result.ShouldHaveValidationErrorFor(x => x.CurrencyOverride);
    }

    [Fact]
    public void Validate_WithValidJsonPaths_PassesValidation()
    {
        var command = new UpdateStore.Command(
            Guid.NewGuid(), "Test", ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null,
                ["$.offers.price"], ["$.name"], ["$.image"]),
            "en-US", false, null
        ) { UserId = Guid.NewGuid() };

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithInvalidJsonPath_FailsValidation()
    {
        var command = new UpdateStore.Command(
            Guid.NewGuid(), "Test", ["example.com"],
            new StoreSelectorDto([".price"], [".name"], [".img"], null, null,
                NameJsonPaths: ["[[[bad"]),
            "en-US", false, null
        ) { UserId = Guid.NewGuid() };

        var result = _validator.TestValidate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Invalid JSONPath expression");
    }
}
