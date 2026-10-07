using System.Linq.Expressions;
using System.Net;
using System.Net.Sockets;
using FluentValidation;
using Ophi.Api.Features.Products;

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
        url != null &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        !IsPrivateOrReservedHost(uri);

    private static bool IsPrivateOrReservedHost(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();

        if (host is "localhost" || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".internal", StringComparison.Ordinal))
            return true;

        // Note: a DNS name that RESOLVES to a private address is not caught here — the check is on
        // the literal host only. Closing that (and re-checking after redirects) needs a resolving
        // handler, tracked separately.
        return IPAddress.TryParse(host, out var ip) && IsPrivateOrReservedAddress(ip);
    }

    internal static bool IsPrivateOrReservedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        // An IPv6 literal carrying an embedded IPv4 address ("[::ffff:169.254.169.254]") reaches
        // exactly the same host as its dotted form, so unwrap it before the range checks — otherwise
        // every IPv4 rule below is trivially bypassed by rewriting the address in v6 form. Covers
        // both the IPv4-mapped form and the deprecated IPv4-compatible form ("[::169.254.169.254]",
        // RFC 4291 §2.5.5.1), which `IsIPv4MappedToIPv6` does not recognize.
        var ip = HasEmbeddedIPv4(address) ? address.MapToIPv4() : address;
        var bytes = ip.GetAddressBytes();

        if (bytes.Length != 4)
        {
            return ip.Equals(IPAddress.IPv6Any)          // :: (unspecified)
                || ip.IsIPv6LinkLocal                    // fe80::/10
                || ip.IsIPv6SiteLocal                    // fec0::/10 (deprecated site-local)
                || (bytes[0] & 0xFE) == 0xFC;            // fc00::/7 unique-local
        }

        return bytes[0] switch
        {
            0 => true,                                               // 0.0.0.0/8 — routes to localhost on Linux
            10 => true,                                              // 10.0.0.0/8
            127 => true,                                             // 127.0.0.0/8
            100 when bytes[1] >= 64 && bytes[1] <= 127 => true,      // 100.64.0.0/10 (CGNAT)
            169 when bytes[1] == 254 => true,                        // 169.254.0.0/16 (link-local + cloud metadata)
            172 when bytes[1] >= 16 && bytes[1] <= 31 => true,       // 172.16.0.0/12
            192 when bytes[1] == 0 && bytes[2] == 0 => true,         // 192.0.0.0/24 (IETF protocol assignments)
            192 when bytes[1] == 168 => true,                        // 192.168.0.0/16
            198 when bytes[1] is 18 or 19 => true,                   // 198.18.0.0/15 (benchmarking)
            _ => false
        };
    }

    /// <summary>
    /// True for an IPv6 address whose low 32 bits are an embedded IPv4 address: the mapped form
    /// (<c>::ffff:a.b.c.d</c>) and the deprecated compatible form (<c>::a.b.c.d</c>, all-zero prefix).
    /// <see cref="IPAddress.MapToIPv4"/> takes the low 4 bytes in both cases, so both unwrap the same
    /// way. <c>::</c> and <c>::1</c> reach here too and unwrap to <c>0.0.0.x</c>, which the
    /// <c>0.0.0.0/8</c> rule blocks anyway.
    /// </summary>
    private static bool HasEmbeddedIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        if (address.IsIPv4MappedToIPv6)
            return true;

        var bytes = address.GetAddressBytes();
        for (var i = 0; i < 12; i++)
        {
            if (bytes[i] != 0) return false;
        }
        return true;
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
