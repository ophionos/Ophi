using FluentAssertions;
using FluentValidation.TestHelper;
using Ophi.Api.Features.Stores;

namespace Ophi.Api.Tests.Unit.Stores;

/// <summary>
/// The frontend keys field errors by property path, so every store slice must report selector
/// failures under the same <c>Selectors.*</c> paths.
/// </summary>
public class StoreSelectorErrorPathTests
{
    private static StoreSelectorDto Selectors(string[]? price = null, string[]? priceJsonPaths = null) =>
        new(price ?? [".price"], [".name"], [".img"], null, null, priceJsonPaths);

    private static CreateStore.Command Create(StoreSelectorDto s) =>
        new("test-store", "Test Store", ["example.com"], s);

    private static UpdateStore.Command Update(StoreSelectorDto s) =>
        new(Guid.NewGuid(), "Test Store", ["example.com"], s);

    private static ImportStore.Command Import(StoreSelectorDto s) =>
        new("test-store", "Test Store", ["example.com"], s);

    [Fact]
    public void CreateStore_WithBlankPriceSelector_ReportsErrorAtSelectorsPriceSelectors()
    {
        var result = new CreateStore.Validator().TestValidate(Create(Selectors(price: [" "])));
        result.Errors.Select(e => e.PropertyName).Should().Contain("Selectors.PriceSelectors");
        result.Errors.Should().Contain(e => e.ErrorMessage == "Price selectors cannot be empty or whitespace");
    }

    [Fact]
    public void UpdateStore_WithBlankPriceSelector_ReportsErrorAtSelectorsPriceSelectors()
    {
        var result = new UpdateStore.Validator().TestValidate(Update(Selectors(price: [" "])));
        result.Errors.Select(e => e.PropertyName).Should().Contain("Selectors.PriceSelectors");
        result.Errors.Should().Contain(e => e.ErrorMessage == "Price selectors cannot be empty or whitespace");
    }

    [Fact]
    public void ImportStore_WithBlankPriceSelector_ReportsErrorAtSelectorsPriceSelectors()
    {
        var result = new ImportStore.Validator().TestValidate(Import(Selectors(price: [" "])));
        result.Errors.Select(e => e.PropertyName).Should().Contain("Selectors.PriceSelectors");
        result.Errors.Should().Contain(e => e.ErrorMessage == "Price selectors cannot be empty or whitespace");
    }

    [Fact]
    public void CreateStore_WithInvalidPriceJsonPath_ReportsErrorUnderSelectorsPriceJsonPaths()
    {
        var result = new CreateStore.Validator().TestValidate(Create(Selectors(priceJsonPaths: ["not a path"])));
        result.Errors.Should().Contain(e => e.PropertyName.StartsWith("Selectors.PriceJsonPaths") && e.ErrorMessage == "Invalid JSONPath expression");
    }

    [Fact]
    public void UpdateStore_WithInvalidPriceJsonPath_ReportsErrorUnderSelectorsPriceJsonPaths()
    {
        var result = new UpdateStore.Validator().TestValidate(Update(Selectors(priceJsonPaths: ["not a path"])));
        result.Errors.Should().Contain(e => e.PropertyName.StartsWith("Selectors.PriceJsonPaths") && e.ErrorMessage == "Invalid JSONPath expression");
    }

    [Fact]
    public void ImportStore_WithInvalidPriceJsonPath_ReportsErrorUnderSelectorsPriceJsonPaths()
    {
        var result = new ImportStore.Validator().TestValidate(Import(Selectors(priceJsonPaths: ["not a path"])));
        result.Errors.Should().Contain(e => e.PropertyName.StartsWith("Selectors.PriceJsonPaths") && e.ErrorMessage == "Invalid JSONPath expression");
    }

    // System.Text.Json leaves an explicit JSON null in a non-nullable array. That must be a 400,
    // not a NullReferenceException inside the validator and a 500.
    [Fact]
    public void CreateStore_WithNullSelectorAndDomainArrays_ReportsErrorsWithoutThrowing()
    {
        var command = new CreateStore.Command("test-store", "Test Store", null!,
            new StoreSelectorDto(null!, null!, null!, null, null));

        var result = new CreateStore.Validator().TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.DomainPatterns);
        result.Errors.Select(e => e.PropertyName).Should().Contain(["Selectors.PriceSelectors", "Selectors.NameSelectors", "Selectors.ImageSelectors"]);
    }

    [Fact]
    public void ImportStore_WithLowercaseCurrencyOverride_FailsValidation()
    {
        // Create and Update already refuse it; the import path stored it unchecked.
        var command = new ImportStore.Command("test-store", "Test Store", ["example.com"], Selectors(), CurrencyOverride: "eur");

        new ImportStore.Validator().TestValidate(command).ShouldHaveValidationErrorFor(x => x.CurrencyOverride);
    }
}
