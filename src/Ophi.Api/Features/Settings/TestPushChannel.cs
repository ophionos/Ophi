using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Push;
using Wolverine;

namespace Ophi.Api.Features.Settings;

public enum PushChannel
{
    Telegram,
    Pushover,
    Ntfy
}

/// <summary>
/// Sends a sample alert through Telegram, Pushover or ntfy to the recipient the user has saved — the push
/// counterpart of <see cref="TestDiscordWebhook"/>. One slice for all three: the flow is identical and
/// only the service and the recipient field differ. ntfy needs no operator setup, so it is always available.
/// </summary>
public static class TestPushChannel
{
    public static void MapTestPushChannelEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/settings/{channel:regex(^(telegram|pushover|ntfy)$)}/test",
            async (string channel, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(Enum.Parse<PushChannel>(channel, ignoreCase: true))
            {
                UserId = context.User.GetUserId()
            };
            var result = await bus.InvokeAsync<Response>(command);
            return Results.Ok(result);
        })
        .WithName("TestPushChannel")
        .WithTags("Settings")
        .WithSummary("Send a test Telegram, Pushover or ntfy message")
        .Produces<Response>()
        .RequireAuthorization()
        .RequireRateLimiting(Common.RateLimitPolicies.OutboundFetch);
    }

    public record Command(PushChannel Channel)
    {
        public Guid UserId { get; init; }
    }

    public record Response(bool Success, string? Error = null);

    public class Handler(
        OphiDbContext dbContext,
        ITelegramService telegram,
        IPushoverService pushover,
        INtfyService ntfy,
        ILogger<Handler> logger)
    {
        private static readonly PushPriceAlert Sample =
            new("Test Product", "https://example.com", 29.99m, 35.00m, "USD", AlertCondition.Below);

        public async Task<Response> Handle(Command command, CancellationToken cancellationToken)
        {
            logger.LogInformation("{Channel} test for user {UserId}", command.Channel, command.UserId);

            var user = await dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken)
                ?? throw new NotFoundException("User not found");

            var (available, recipient, missing) = command.Channel switch
            {
                PushChannel.Telegram => (telegram.IsConfigured, user.TelegramChatId, "No Telegram chat id saved. Save your chat id first."),
                PushChannel.Pushover => (pushover.IsConfigured, user.PushoverUserKey, "No Pushover user key saved. Save your user key first."),
                _ => (true, user.NtfyTopicUrl, "No ntfy topic URL saved. Save your topic URL first.")
            };

            if (!available)
            {
                return new Response(false, $"{command.Channel} is not set up on this server.");
            }

            if (string.IsNullOrWhiteSpace(recipient))
            {
                return new Response(false, missing);
            }

            try
            {
                var send = command.Channel switch
                {
                    PushChannel.Telegram => telegram.SendPriceAlertAsync(Sample, recipient, cancellationToken),
                    PushChannel.Pushover => pushover.SendPriceAlertAsync(Sample, recipient, cancellationToken),
                    _ => ntfy.SendPriceAlertAsync(Sample, recipient, cancellationToken)
                };
                await send;

                return new Response(true);
            }
            catch (HttpRequestException ex)
            {
                return new Response(false, $"{command.Channel} returned an error: {ex.Message}");
            }
        }
    }
}
