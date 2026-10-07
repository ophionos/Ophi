using FluentValidation;
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
}
