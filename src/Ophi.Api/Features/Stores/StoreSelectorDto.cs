using FluentValidation;
using Json.Path;
using Ophi.Infrastructure.Persistence.Configurations;
using Ophi.Infrastructure.Scraping.Adapters;

namespace Ophi.Api.Features.Stores;

/// <summary>The selector block shared by the create, update and import store slices.</summary>
public record StoreSelectorDto(
    string[] PriceSelectors,
    string[] NameSelectors,
    string[] ImageSelectors,
    string[]? PriceRegexPatterns,
    string[]? ImageRegexPatterns,
    string[]? PriceJsonPaths = null,
    string[]? NameJsonPaths = null,
    string[]? ImageJsonPaths = null
)
{
    public StoreSelectorConfig ToConfig() => new()
    {
        PriceSelectors = PriceSelectors,
        NameSelectors = NameSelectors,
        ImageSelectors = ImageSelectors,
        PriceRegexPatterns = PriceRegexPatterns,
        ImageRegexPatterns = ImageRegexPatterns,
        PriceJsonPaths = PriceJsonPaths,
        NameJsonPaths = NameJsonPaths,
        ImageJsonPaths = ImageJsonPaths
    };
}

/// <summary>
/// The one owner of the store-slice validation rules. Child errors surface as
/// <c>Selectors.PriceSelectors</c> etc. because the slices attach this via <c>SetValidator</c>.
/// </summary>
public class StoreSelectorDtoValidator : AbstractValidator<StoreSelectorDto>
{
    // Cascade.Stop on the array rules: a JSON null passes the non-nullable annotation, and the
    // Must(...All) after a failed NotEmpty would throw on it (a 500 instead of a 400).
    public StoreSelectorDtoValidator()
    {
        RuleFor(x => x.PriceSelectors)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("At least one price selector is required")
            .Must(s => s.All(v => !string.IsNullOrWhiteSpace(v)))
            .WithMessage("Price selectors cannot be empty or whitespace");

        RuleFor(x => x.NameSelectors)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("At least one name selector is required")
            .Must(s => s.All(v => !string.IsNullOrWhiteSpace(v)))
            .WithMessage("Name selectors cannot be empty or whitespace");

        RuleFor(x => x.ImageSelectors)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("At least one image selector is required")
            .Must(s => s.All(v => !string.IsNullOrWhiteSpace(v)))
            .WithMessage("Image selectors cannot be empty or whitespace");

        RuleForEach(x => x.PriceJsonPaths)
            .Must(IsValidJsonPath)
            .WithMessage("Invalid JSONPath expression")
            .When(x => x.PriceJsonPaths is { Length: > 0 });

        RuleForEach(x => x.NameJsonPaths)
            .Must(IsValidJsonPath)
            .WithMessage("Invalid JSONPath expression")
            .When(x => x.NameJsonPaths is { Length: > 0 });

        RuleForEach(x => x.ImageJsonPaths)
            .Must(IsValidJsonPath)
            .WithMessage("Invalid JSONPath expression")
            .When(x => x.ImageJsonPaths is { Length: > 0 });
    }

    private static bool IsValidJsonPath(string? p) => !string.IsNullOrWhiteSpace(p) && JsonPath.TryParse(p, out _);
}

/// <summary>Rule-builder extensions for the fields every store slice validates identically.</summary>
internal static class StoreRuleExtensions
{
    public static IRuleBuilderOptions<T, string[]> MustBeValidDomainPatterns<T>(this IRuleBuilderInitial<T, string[]> rule) =>
        rule.Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("At least one domain pattern is required")
            .Must(patterns => patterns.All(p => !string.IsNullOrWhiteSpace(p)))
            .WithMessage("Domain patterns cannot be empty")
            .Must(StoreValidationHelper.DomainPatternsFitColumn)
            .WithMessage(StoreValidationHelper.DomainPatternsTooLongMessage);

    public static IRuleBuilderOptions<T, StoreSelectorDto> MustBeValidSelectors<T>(this IRuleBuilder<T, StoreSelectorDto> rule) =>
        rule.Must(s => s is null || StoreValidationHelper.SelectorsFitColumn(s.ToConfig()))
            .WithMessage(StoreValidationHelper.SelectorsTooLongMessage)
            .NotNull().WithMessage("Selectors are required")
            .SetValidator(new StoreSelectorDtoValidator());

    public static IRuleBuilderOptions<T, string?> MustFitPriceLocale<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(StoreConfigurationConfiguration.PriceLocaleMaxLength)
            .WithMessage(StoreValidationHelper.PriceLocaleTooLongMessage);

    public static IRuleBuilderOptions<T, string?> MustBeCurrencyCode<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Matches(@"^[A-Z]{3}$")
            .When(currency => currency != null)
            .WithMessage("Currency override must be a 3-letter uppercase ISO 4217 code (e.g., EUR, USD)");
}
