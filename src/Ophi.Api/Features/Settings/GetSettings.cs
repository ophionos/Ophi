using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Email;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Push;
using Wolverine;

namespace Ophi.Api.Features.Settings;

public static class GetSettings
{
    public static void MapGetSettingsEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/settings", async (IMessageBus bus, HttpContext context) =>
        {
            var query = new Query { UserId = context.User.GetUserId() };
            var result = await bus.InvokeAsync<Response>(query);
            return Results.Ok(result);
        })
        .WithName("GetSettings")
        .WithTags("Settings")
        .WithSummary("Get user settings")
        .Produces<Response>()
        .RequireAuthorization();
    }

    public record Query
    {
        public Guid UserId { get; init; }
    }

    /// <param name="EmailConfigured">
    /// Whether the server has SMTP set up. Server-level configuration, not a per-user field —
    /// this boolean is the whole of what is exposed; host, user and password never leave the server.
    /// </param>
    /// <param name="EmailNotificationsEnabled">
    /// The account's own opt-in, independent of <paramref name="EmailConfigured"/>. A user can opt
    /// out on a server with working SMTP, and an opted-in user can sit on a server with none.
    /// </param>
    /// <param name="TelegramAvailable">The server has a bot token; the card is hidden otherwise.</param>
    /// <param name="TelegramConfigured">
    /// The user has a chat id set. Recipients are reported as booleans only, like the Discord URL.
    /// </param>
    public record Response(
        bool AffiliatesEnabled,
        int? DefaultCheckIntervalMinutes,
        int? PageFetchDelaySeconds,
        int? ScrapeCacheTtlMinutes,
        bool DiscordWebhookConfigured,
        bool DiscordNotificationsEnabled,
        int? AnomalyThresholdPercent,
        int? AutoPauseAfterFailures,
        bool EmailConfigured,
        bool EmailNotificationsEnabled,
        bool TelegramAvailable,
        string? TelegramBotUsername,
        bool TelegramConfigured,
        bool TelegramNotificationsEnabled,
        bool PushoverAvailable,
        bool PushoverConfigured,
        bool PushoverNotificationsEnabled,
        string? DisplayCurrency,
        bool NtfyConfigured,
        bool NtfyNotificationsEnabled);

    public class Handler(
        OphiDbContext dbContext,
        IOptions<EmailSettings> emailSettings,
        ITelegramService telegram,
        IPushoverService pushover,
        ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query query, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching settings for user {UserId}", query.UserId);

            var user = await dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == query.UserId, cancellationToken)
                ?? throw new NotFoundException("User not found");

            return new Response(
                user.AffiliatesEnabled,
                user.DefaultCheckIntervalMinutes,
                user.PageFetchDelaySeconds,
                user.ScrapeCacheTtlMinutes,
                !string.IsNullOrWhiteSpace(user.DiscordWebhookUrl),
                user.DiscordNotificationsEnabled,
                user.AnomalyThresholdPercent,
                user.AutoPauseAfterFailures,
                emailSettings.Value.IsConfigured,
                user.EmailNotificationsEnabled,
                telegram.IsConfigured,
                telegram.BotUsername,
                !string.IsNullOrWhiteSpace(user.TelegramChatId),
                user.TelegramNotificationsEnabled,
                pushover.IsConfigured,
                !string.IsNullOrWhiteSpace(user.PushoverUserKey),
                user.PushoverNotificationsEnabled,
                user.DisplayCurrency,
                !string.IsNullOrWhiteSpace(user.NtfyTopicUrl),
                user.NtfyNotificationsEnabled);
        }
    }
}
