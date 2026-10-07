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
    Pushover
}

/// <summary>
/// Sends a sample alert through Telegram or Pushover to the recipient the user has saved — the push
/// counterpart of <see cref="TestDiscordWebhook"/>. One slice for both channels: the flow is identical
/// and only the service and the recipient field differ.
/// </summary>
public static class TestPushChannel
{
    public static void MapTestPushChannelEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/settings/{channel:regex(^(telegram|pushover)$)}/test",
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
        .WithSummary("Send a test Telegram or Pushover message")
        .Produces<Response>()
        .RequireAuthorization();
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

            var (available, recipient, missing) = command.Channel == PushChannel.Telegram
                ? (telegram.IsConfigured, user.TelegramChatId, "No Telegram chat id saved. Save your chat id first.")
                : (pushover.IsConfigured, user.PushoverUserKey, "No Pushover user key saved. Save your user key first.");

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
                if (command.Channel == PushChannel.Telegram)
                    await telegram.SendPriceAlertAsync(Sample, recipient, cancellationToken);
                else
                    await pushover.SendPriceAlertAsync(Sample, recipient, cancellationToken);

                return new Response(true);
            }
            catch (HttpRequestException ex)
            {
                return new Response(false, $"{command.Channel} returned an error: {ex.Message}");
            }
        }
    }
}
