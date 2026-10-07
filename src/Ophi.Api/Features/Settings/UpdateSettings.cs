using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Common.Validators;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Settings;

public static class UpdateSettings
{
    public static void MapUpdateSettingsEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPut("/api/v1/settings", async (Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(
                request.AffiliatesEnabled,
                request.DefaultCheckIntervalMinutes,
                request.PageFetchDelaySeconds,
                request.ScrapeCacheTtlMinutes,
                request.DiscordWebhookUrl,
                request.DiscordNotificationsEnabled,
                request.AnomalyThresholdPercent,
                request.AutoPauseAfterFailures,
                request.EmailNotificationsEnabled,
                request.TelegramChatId,
                request.TelegramNotificationsEnabled,
                request.PushoverUserKey,
                request.PushoverNotificationsEnabled,
                request.DisplayCurrency
            )
            {
                UserId = context.User.GetUserId()
            };
            var result = await bus.InvokeAsync<Response>(command);
            return Results.Ok(result);
        })
        .WithName("UpdateSettings")
        .WithTags("Settings")
        .WithSummary("Update user settings")
        .WithDescription("Updates user settings. All fields are optional — only provided fields are updated. Set defaultCheckIntervalMinutes, pageFetchDelaySeconds, or scrapeCacheTtlMinutes to 0 to reset to system default.")
        .Produces<Response>()
        .RequireAuthorization();
    }

    public record Request(
        bool? AffiliatesEnabled = null,
        int? DefaultCheckIntervalMinutes = null,
        int? PageFetchDelaySeconds = null,
        int? ScrapeCacheTtlMinutes = null,
        string? DiscordWebhookUrl = null,
        bool? DiscordNotificationsEnabled = null,
        int? AnomalyThresholdPercent = null,
        int? AutoPauseAfterFailures = null,
        bool? EmailNotificationsEnabled = null,
        string? TelegramChatId = null,
        bool? TelegramNotificationsEnabled = null,
        string? PushoverUserKey = null,
        bool? PushoverNotificationsEnabled = null,
        string? DisplayCurrency = null
    );

    public record Command(
        bool? AffiliatesEnabled,
        int? DefaultCheckIntervalMinutes,
        int? PageFetchDelaySeconds = null,
        int? ScrapeCacheTtlMinutes = null,
        string? DiscordWebhookUrl = null,
        bool? DiscordNotificationsEnabled = null,
        int? AnomalyThresholdPercent = null,
        int? AutoPauseAfterFailures = null,
        bool? EmailNotificationsEnabled = null,
        string? TelegramChatId = null,
        bool? TelegramNotificationsEnabled = null,
        string? PushoverUserKey = null,
        bool? PushoverNotificationsEnabled = null,
        string? DisplayCurrency = null
    )
    {
        public Guid UserId { get; init; }
    }

    public record Response(
        bool AffiliatesEnabled,
        int? DefaultCheckIntervalMinutes,
        int? PageFetchDelaySeconds,
        int? ScrapeCacheTtlMinutes,
        bool DiscordWebhookConfigured,
        bool DiscordNotificationsEnabled,
        int? AnomalyThresholdPercent,
        int? AutoPauseAfterFailures,
        bool EmailNotificationsEnabled,
        bool TelegramConfigured,
        bool TelegramNotificationsEnabled,
        bool PushoverConfigured,
        bool PushoverNotificationsEnabled,
        string? DisplayCurrency);

    public class Validator : AbstractValidator<Command>
    {
        private const string DiscordWebhookPrefix = "https://discord.com/api/webhooks/";
        private const string DiscordAppWebhookPrefix = "https://discordapp.com/api/webhooks/";

        public Validator()
        {
            When(x => x.DefaultCheckIntervalMinutes.HasValue && x.DefaultCheckIntervalMinutes.Value != 0, () =>
            {
                RuleFor(x => x.DefaultCheckIntervalMinutes)
                    .Must(v => v >= ProductValidationRules.CheckIntervalMinutesMin && v <= ProductValidationRules.CheckIntervalMinutesMax)
                    .WithMessage($"Check interval must be between {ProductValidationRules.CheckIntervalMinutesMin} and {ProductValidationRules.CheckIntervalMinutesMax} minutes (or 0 to reset to default)");
            });

            When(x => x.PageFetchDelaySeconds.HasValue && x.PageFetchDelaySeconds.Value != 0, () =>
            {
                RuleFor(x => x.PageFetchDelaySeconds)
                    .Must(v => v >= 1 && v <= ProductValidationRules.PageFetchDelaySecondsMax)
                    .WithMessage($"Page fetch delay must be between 1 and {ProductValidationRules.PageFetchDelaySecondsMax} seconds (or 0 to reset to default)");
            });

            When(x => x.ScrapeCacheTtlMinutes.HasValue && x.ScrapeCacheTtlMinutes.Value != 0, () =>
            {
                RuleFor(x => x.ScrapeCacheTtlMinutes)
                    .Must(v => v >= 1 && v <= ProductValidationRules.ScrapeCacheTtlMinutesMax)
                    .WithMessage($"Scrape cache TTL must be between 1 and {ProductValidationRules.ScrapeCacheTtlMinutesMax} minutes (or 0 to reset to default)");
            });

            When(x => x.DiscordWebhookUrl != null && x.DiscordWebhookUrl != "", () =>
            {
                RuleFor(x => x.DiscordWebhookUrl)
                    .Must(url => url!.StartsWith(DiscordWebhookPrefix, StringComparison.OrdinalIgnoreCase) ||
                                 url.StartsWith(DiscordAppWebhookPrefix, StringComparison.OrdinalIgnoreCase))
                    .WithMessage("Discord webhook URL must be a valid Discord webhook URL (https://discord.com/api/webhooks/...)");
            });

            // A numeric chat id (negative for groups) or a public @channel username.
            When(x => !string.IsNullOrEmpty(x.TelegramChatId), () =>
            {
                RuleFor(x => x.TelegramChatId)
                    .Matches(@"^(-?\d{1,20}|@[A-Za-z0-9_]{5,32})$")
                    .WithMessage("Telegram chat id must be a number (e.g. 123456789) or an @channel name");
            });

            // Pushover user and group keys are 30 alphanumeric characters.
            When(x => !string.IsNullOrEmpty(x.PushoverUserKey), () =>
            {
                RuleFor(x => x.PushoverUserKey)
                    .Matches("^[A-Za-z0-9]{30}$")
                    .WithMessage("Pushover user key must be 30 letters and digits");
            });

            // Only currencies the ECB publishes a rate for — anything else could not be converted.
            When(x => !string.IsNullOrEmpty(x.DisplayCurrency), () =>
            {
                RuleFor(x => x.DisplayCurrency)
                    .Must(c => Ophi.Api.Features.Fx.GetFxRates.SupportedCurrencies.Contains(c!))
                    .WithMessage("Display currency must be one the ECB publishes a rate for (e.g. USD, EUR, GBP)");
            });

            When(x => x.AnomalyThresholdPercent.HasValue && x.AnomalyThresholdPercent.Value != 0, () =>
            {
                RuleFor(x => x.AnomalyThresholdPercent)
                    .Must(v => v >= 10 && v <= 500)
                    .WithMessage("Anomaly threshold must be between 10 and 500 percent (or 0 to reset to default)");
            });

            When(x => x.AutoPauseAfterFailures.HasValue && x.AutoPauseAfterFailures.Value != 0, () =>
            {
                RuleFor(x => x.AutoPauseAfterFailures)
                    .Must(v => v >= 1 && v <= 50)
                    .WithMessage("Auto-pause after failures must be between 1 and 50 (or 0 to reset to default)");
            });
        }
    }

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command command, CancellationToken cancellationToken)
        {
            var user = await dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken)
                ?? throw new NotFoundException("User not found");

            if (command.AffiliatesEnabled.HasValue)
            {
                user.AffiliatesEnabled = command.AffiliatesEnabled.Value;
            }

            if (command.DefaultCheckIntervalMinutes.HasValue)
            {
                user.DefaultCheckIntervalMinutes = command.DefaultCheckIntervalMinutes.Value == 0
                    ? null
                    : command.DefaultCheckIntervalMinutes.Value;
            }

            if (command.PageFetchDelaySeconds.HasValue)
            {
                user.PageFetchDelaySeconds = command.PageFetchDelaySeconds.Value == 0
                    ? null
                    : command.PageFetchDelaySeconds.Value;
            }

            if (command.ScrapeCacheTtlMinutes.HasValue)
            {
                user.ScrapeCacheTtlMinutes = command.ScrapeCacheTtlMinutes.Value == 0
                    ? null
                    : command.ScrapeCacheTtlMinutes.Value;
            }

            if (command.DiscordWebhookUrl != null)
            {
                user.DiscordWebhookUrl = string.IsNullOrWhiteSpace(command.DiscordWebhookUrl)
                    ? null
                    : command.DiscordWebhookUrl;
            }

            if (command.DiscordNotificationsEnabled.HasValue)
            {
                user.DiscordNotificationsEnabled = command.DiscordNotificationsEnabled.Value;
            }

            if (command.AnomalyThresholdPercent.HasValue)
            {
                user.AnomalyThresholdPercent = command.AnomalyThresholdPercent.Value == 0
                    ? null
                    : command.AnomalyThresholdPercent.Value;
            }

            if (command.AutoPauseAfterFailures.HasValue)
            {
                user.AutoPauseAfterFailures = command.AutoPauseAfterFailures.Value == 0
                    ? null
                    : command.AutoPauseAfterFailures.Value;
            }

            if (command.EmailNotificationsEnabled.HasValue)
            {
                user.EmailNotificationsEnabled = command.EmailNotificationsEnabled.Value;
            }

            // Same convention as the Discord URL: null leaves it, "" clears it.
            if (command.TelegramChatId != null)
            {
                user.TelegramChatId = string.IsNullOrWhiteSpace(command.TelegramChatId) ? null : command.TelegramChatId.Trim();
            }

            if (command.TelegramNotificationsEnabled.HasValue)
            {
                user.TelegramNotificationsEnabled = command.TelegramNotificationsEnabled.Value;
            }

            if (command.PushoverUserKey != null)
            {
                user.PushoverUserKey = string.IsNullOrWhiteSpace(command.PushoverUserKey) ? null : command.PushoverUserKey.Trim();
            }

            if (command.PushoverNotificationsEnabled.HasValue)
            {
                user.PushoverNotificationsEnabled = command.PushoverNotificationsEnabled.Value;
            }

            if (command.DisplayCurrency != null)
            {
                user.DisplayCurrency = string.IsNullOrWhiteSpace(command.DisplayCurrency)
                    ? null
                    : command.DisplayCurrency.Trim().ToUpperInvariant();
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Settings updated for user {UserId}", command.UserId);

            return new Response(
                user.AffiliatesEnabled,
                user.DefaultCheckIntervalMinutes,
                user.PageFetchDelaySeconds,
                user.ScrapeCacheTtlMinutes,
                !string.IsNullOrWhiteSpace(user.DiscordWebhookUrl),
                user.DiscordNotificationsEnabled,
                user.AnomalyThresholdPercent,
                user.AutoPauseAfterFailures,
                user.EmailNotificationsEnabled,
                !string.IsNullOrWhiteSpace(user.TelegramChatId),
                user.TelegramNotificationsEnabled,
                !string.IsNullOrWhiteSpace(user.PushoverUserKey),
                user.PushoverNotificationsEnabled,
                user.DisplayCurrency);
        }
    }
}
