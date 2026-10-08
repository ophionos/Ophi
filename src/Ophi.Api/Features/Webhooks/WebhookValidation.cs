using FluentValidation;
using Ophi.Api.Common.Validators;
using Ophi.Infrastructure.Net;
using Ophi.Infrastructure.Webhooks;

namespace Ophi.Api.Features.Webhooks;

internal static class WebhookValidation
{
    private static readonly HashSet<string> ValidEvents =
        [WebhookEvents.AlertFired, WebhookEvents.PriceChanged, WebhookEvents.ScrapeFailed];

    private static readonly string ValidEventsList = string.Join(", ", ValidEvents);

    internal static IRuleBuilderOptions<T, List<string>> MustContainValidEvents<T>(this IRuleBuilder<T, List<string>> rule) =>
        rule.Must(events => events.All(e => ValidEvents.Contains(e, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Events must be one of: {ValidEventsList}");

    /// <summary>
    /// The product URL rule, but with the webhook client's address policy: networks the operator listed
    /// in <c>Webhooks:AllowedNetworks</c> pass, and so do <c>.local</c> / <c>.internal</c> names once any
    /// network is listed.
    /// </summary>
    internal static IRuleBuilderOptions<T, string> MustBeAllowedWebhookUrl<T>(
        this IRuleBuilder<T, string> rule, WebhookAddressPolicy addressPolicy) =>
        rule.Must(url => ProductValidationRules.IsValidHttpUrl(
                url, addressPolicy.IsBlocked, allowLocalNames: addressPolicy.AllowedNetworks.Count > 0))
            .WithMessage("URL must be a valid HTTP or HTTPS URL on a public network, or on a network the operator allowed");
}
