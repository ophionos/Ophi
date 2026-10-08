using FluentValidation.TestHelper;
using Ophi.Api.Features.Stores;

namespace Ophi.Api.Tests.Unit.Stores;

/// <summary>
/// The store slices serialize domain patterns and selectors into bounded varchar columns. Input
/// that serializes longer than the column passes SQLite (which ignores the bound) and fails Postgres
/// with a 500, so the validators must reject it as a 400 first.
/// </summary>
public class StoreColumnLimitValidatorTests
{
    // 3000 characters of selectors — over the 10000-char column once there are four of them.
    private static readonly string[] HugeSelectors = [new string('a', 3000), new string('b', 3000), new string('c', 3000), new string('d', 3000)];
    private static readonly string[] HugeDomains = Enumerable.Range(0, 200).Select(i => $"shop-{i}.example.com").ToArray();
    private const string LongLocale = "ca-ES-valencia"; // a real culture name, longer than the 10-char column

    private static CreateStore.StoreSelectorDto CreateSelectors(string[]? price = null) =>
        new(price ?? [".price"], [".name"], [".img"], null, null);

    private static CreateStore.Command Create(string[]? domains = null, string[]? price = null, string locale = "en-US") =>
        new("test-store", "Test Store", domains ?? ["example.com"], CreateSelectors(price), locale);

    private static UpdateStore.Command Update(string[]? domains = null, string[]? price = null, string locale = "en-US") =>
        new(Guid.NewGuid(), "Test Store", domains ?? ["example.com"],
            new UpdateStore.StoreSelectorDto(price ?? [".price"], [".name"], [".img"], null, null), locale);

    private static ImportStore.Command Import(string[]? domains = null, string[]? price = null, string locale = "en-US") =>
        new("test-store", "Test Store", domains ?? ["example.com"], CreateSelectors(price), locale);

    [Fact]
    public void CreateStore_WithSelectorsLongerThanColumn_FailsValidation() =>
        new CreateStore.Validator().TestValidate(Create(price: HugeSelectors)).ShouldHaveValidationErrorFor(x => x.Selectors);

    [Fact]
    public void UpdateStore_WithSelectorsLongerThanColumn_FailsValidation() =>
        new UpdateStore.Validator().TestValidate(Update(price: HugeSelectors)).ShouldHaveValidationErrorFor(x => x.Selectors);

    [Fact]
    public void ImportStore_WithSelectorsLongerThanColumn_FailsValidation() =>
        new ImportStore.Validator().TestValidate(Import(price: HugeSelectors)).ShouldHaveValidationErrorFor(x => x.Selectors);

    [Fact]
    public void CreateStore_WithDomainPatternsLongerThanColumn_FailsValidation() =>
        new CreateStore.Validator().TestValidate(Create(domains: HugeDomains)).ShouldHaveValidationErrorFor(x => x.DomainPatterns);

    [Fact]
    public void UpdateStore_WithDomainPatternsLongerThanColumn_FailsValidation() =>
        new UpdateStore.Validator().TestValidate(Update(domains: HugeDomains)).ShouldHaveValidationErrorFor(x => x.DomainPatterns);

    [Fact]
    public void ImportStore_WithDomainPatternsLongerThanColumn_FailsValidation() =>
        new ImportStore.Validator().TestValidate(Import(domains: HugeDomains)).ShouldHaveValidationErrorFor(x => x.DomainPatterns);

    [Fact]
    public void CreateStore_WithPriceLocaleLongerThanColumn_FailsValidation() =>
        new CreateStore.Validator().TestValidate(Create(locale: LongLocale)).ShouldHaveValidationErrorFor(x => x.PriceLocale);

    [Fact]
    public void UpdateStore_WithPriceLocaleLongerThanColumn_FailsValidation() =>
        new UpdateStore.Validator().TestValidate(Update(locale: LongLocale)).ShouldHaveValidationErrorFor(x => x.PriceLocale);

    [Fact]
    public void ImportStore_WithPriceLocaleLongerThanColumn_FailsValidation() =>
        new ImportStore.Validator().TestValidate(Import(locale: LongLocale)).ShouldHaveValidationErrorFor(x => x.PriceLocale);

    [Fact]
    public void CreateStore_WithOrdinaryInput_PassesColumnLimits()
    {
        var result = new CreateStore.Validator().TestValidate(Create());
        result.ShouldNotHaveValidationErrorFor(x => x.Selectors);
        result.ShouldNotHaveValidationErrorFor(x => x.DomainPatterns);
        result.ShouldNotHaveValidationErrorFor(x => x.PriceLocale);
    }
}
