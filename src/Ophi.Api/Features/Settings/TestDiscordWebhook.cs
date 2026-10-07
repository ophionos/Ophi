using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Discord;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Settings;

public static class TestDiscordWebhook
{
    public static void MapTestDiscordWebhookEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/settings/discord/test", async (IMessageBus bus, HttpContext context) =>
        {
            var command = new Command { UserId = context.User.GetUserId() };
            var result = await bus.InvokeAsync<Response>(command);
            return Results.Ok(result);
        })
        .WithName("TestDiscordWebhook")
        .WithTags("Settings")
        .WithSummary("Send a test Discord webhook message")
        .Produces<Response>()
        .RequireAuthorization();
    }

    public record Command
    {
        public Guid UserId { get; init; }
    }

    public record Response(bool Success, string? Error = null);

    public class Handler(OphiDbContext dbContext, IDiscordService discordService, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command command, CancellationToken cancellationToken)
        {
            logger.LogInformation("Discord webhook test for user {UserId}", command.UserId);

            var user = await dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken)
                ?? throw new NotFoundException("User not found");

            if (string.IsNullOrWhiteSpace(user.DiscordWebhookUrl))
            {
                return new Response(false, "No Discord webhook URL configured. Please set a webhook URL first.");
            }

            try
            {
                await discordService.SendPriceAlertAsync(
                    new DiscordPriceAlert("Test Product", "https://example.com", 29.99m, 35.00m, "USD"),
                    user.DiscordWebhookUrl,
                    cancellationToken);

                return new Response(true);
            }
            catch (HttpRequestException ex)
            {
                return new Response(false, $"Discord returned an error: {ex.Message}");
            }
            catch
            {
                return new Response(false, "Failed to send test message. Please verify your webhook URL.");
            }
        }
    }
}
