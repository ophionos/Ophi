using System.Linq.Expressions;
using System.Net;
using FluentValidation;
using Ophi.Api.Features.Products;
using Ophi.Infrastructure.Net;

namespace Ophi.Api.Common.Validators;

public static class ProductValidationRules
{
    public const string CurrencyPattern = "^[A-Z]{3}$";
    public const string CurrencyMessage = "Currency must be a 3-letter uppercase code (e.g., USD, EUR)";

    public const int CheckIntervalMinutesMin = 15;
    public const int CheckIntervalMinutesMax = 1440;
    public const int PageFetchDelaySecondsMax = 30;
    public const int ScrapeCacheTtlMinutesMax = 1440;

    internal static bool IsValidHttpUrl(string? url) =>
        IsValidHttpUrl(url, AddressPolicy.IsBlocked, allowLocalNames: false);

    /// <param name="isBlocked">The address policy of the client that will fetch the URL.</param>
    /// <param name="allowLocalNames">Accept <c>.local</c> / <c>.internal</c> names, for a client whose
    /// policy re-opens private networks; the connect-time check still decides.</param>
    internal static bool IsValidHttpUrl(string? url, Func<IPAddress, bool> isBlocked, bool allowLocalNames) =>
        url != null &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        !IsPrivateOrReservedHost(uri, isBlocked, allowLocalNames);

    private static bool IsPrivateOrReservedHost(Uri uri, Func<IPAddress, bool> isBlocked, bool allowLocalNames)
    {
        var host = uri.Host.ToLowerInvariant();

        if (host is "localhost")
            return true;

        if (!allowLocalNames && (host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".internal", StringComparison.Ordinal)))
            return true;

        // Literal host only, for a fast 400. A DNS name that resolves to a private address (and every
        // redirect hop) is refused at connect time by PublicAddressHandler, the real control.
        return IPAddress.TryParse(host.Trim('[', ']'), out var ip) && isBlocked(ip);
    }

    public static IRuleBuilderOptions<T, string> MustBeValidHttpUrl<T>(
        this IRuleBuilder<T, string> ruleBuilder,
        string message = "Must be a valid HTTP or HTTPS URL") =>
        ruleBuilder
            .Must(url => IsValidHttpUrl(url))
            .WithMessage(message);

    public static void ApplyCustomFieldsRules<T>(
        AbstractValidator<T> validator,
        Expression<Func<T, List<CustomFieldDto>?>> selector)
    {
        var getList = selector.Compile();

        // RuleForEach needs IEnumerable<CustomFieldDto> — create a compatible expression
        var enumerableSelector = Expression.Lambda<Func<T, IEnumerable<CustomFieldDto>>>(
            Expression.Convert(selector.Body, typeof(IEnumerable<CustomFieldDto>)),
            selector.Parameters);

        validator.When(x => getList(x) != null, () =>
        {
            validator.RuleFor(selector)
                .Must(f => f!.Count <= 50).WithMessage("Cannot have more than 50 custom fields")
                .Must(f => f!.Select(cf => cf.Name.ToLowerInvariant()).Distinct().Count() == f!.Count)
                .WithMessage("Custom field names must be unique");

            validator.RuleForEach(enumerableSelector).ChildRules(field =>
            {
                field.RuleFor(f => f.Name)
                    .NotEmpty().WithMessage("Custom field name is required")
                    .MaximumLength(100).WithMessage("Custom field name must not exceed 100 characters");

                field.RuleFor(f => f.Value)
                    .MaximumLength(1000).WithMessage("Custom field value must not exceed 1000 characters");
            });
        });
    }
}
