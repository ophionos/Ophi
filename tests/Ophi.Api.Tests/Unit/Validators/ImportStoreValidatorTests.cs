using FluentAssertions;
using FluentValidation.TestHelper;
using Ophi.Api.Features.Stores;

namespace Ophi.Api.Tests.Unit.Validators;

public class ImportStoreValidatorTests
{
    private readonly ImportStore.Validator _validator = new();

    private static ImportStore.Command CreateValidCommand() => new(
        "my-store",
        "My Store",
        ["mystore.com"],
        new CreateStore.StoreSelectorDto(
            [".price"],
            [".name"],
            [".img"],
            null,
            null
        )
    );

    [Fact]
    public void Validate_WithValidData_ShouldNotHaveErrors()
    {
        var command = CreateValidCommand();
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyStoreId_ShouldHaveError()
    {
        var command = CreateValidCommand() with { StoreId = "" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.StoreId);
    }

    [Fact]
    public void Validate_WithBuiltInStoreId_ShouldHaveError()
    {
        var command = CreateValidCommand() with { StoreId = "amazon" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.StoreId);
    }

    [Fact]
    public void Validate_WithInvalidStoreIdFormat_ShouldHaveError()
    {
        var command = CreateValidCommand() with { StoreId = "My Store!" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.StoreId);
    }

    [Fact]
    public void Validate_WithEmptyName_ShouldHaveError()
    {
        var command = CreateValidCommand() with { Name = "" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_WithEmptyDomainPatterns_ShouldHaveError()
    {
        var command = CreateValidCommand() with { DomainPatterns = [] };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.DomainPatterns);
    }

    [Fact]
    public void Validate_WithNullSelectors_ShouldHaveError()
    {
        var command = CreateValidCommand() with { Selectors = null! };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Selectors);
    }

    [Fact]
    public void Validate_WithEmptyPriceSelectors_ShouldHaveError()
    {
        var command = CreateValidCommand() with
        {
            Selectors = new CreateStore.StoreSelectorDto([], [".name"], [".img"], null, null)
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Selectors.PriceSelectors);
    }

    [Fact]
    public void Validate_WithValidJsonPaths_ShouldNotHaveErrors()
    {
        var command = CreateValidCommand() with
        {
            Selectors = new CreateStore.StoreSelectorDto(
                [".price"], [".name"], [".img"], null, null,
                ["$.offers.price"], ["$.name"], ["$.image"])
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithInvalidJsonPath_ShouldHaveError()
    {
        var command = CreateValidCommand() with
        {
            Selectors = new CreateStore.StoreSelectorDto(
                [".price"], [".name"], [".img"], null, null,
                ImageJsonPaths: ["[[[bad"])
        };
        var result = _validator.TestValidate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Invalid JSONPath expression");
    }
}
