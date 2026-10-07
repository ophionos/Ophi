using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Email;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Settings;

/// <summary>
/// Sends a test email so a self-hoster can tell a working SMTP setup from a silently dead one —
/// today the only way to discover email is broken is to never receive a price alert.
/// </summary>
public static class SendTestEmail
{
    public static void MapSendTestEmailEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/settings/email/test", async (IMessageBus bus, HttpContext context) =>
        {
            var command = new Command { UserId = context.User.GetUserId() };
            var result = await bus.InvokeAsync<Response>(command);
            return Results.Ok(result);
        })
        .WithName("SendTestEmail")
        .WithTags("Settings")
        .WithSummary("Send a test email to the signed-in user's own account address")
        .Produces<Response>()
        .RequireAuthorization();
    }

    /// <summary>
    /// Carries the authenticated user's id and nothing else — deliberately. The recipient is
    /// resolved server-side from that user's account email, so no caller can nominate an address;
    /// accepting one would make the instance an open relay / spam vector. Do not add a recipient
    /// field here.
    /// </summary>
    public record Command
    {
        public Guid UserId { get; init; }
    }

    /// <param name="SentTo">The user's own account address, echoed back so the UI can say where to look.</param>
    public record Response(bool Success, string? Error = null, string? SentTo = null);

    public class Handler(
        OphiDbContext dbContext,
        IEmailService emailService,
        IOptions<EmailSettings> emailSettings,
        ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command command, CancellationToken cancellationToken)
        {
            logger.LogInformation("Test email requested by user {UserId}", command.UserId);

            var user = await dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken)
                ?? throw new NotFoundException("User not found");

            if (!emailSettings.Value.IsConfigured)
            {
                return new Response(false,
                    "Email is not configured on this server. Set SMTP_HOST, SMTP_USER, SMTP_PASS and SMTP_FROM, then restart.");
            }

            try
            {
                await emailService.SendTestEmailAsync(user.Email, user.Name, cancellationToken);
                return new Response(true, SentTo: user.Email);
            }
            catch (Exception ex)
            {
                // The SMTP failure text IS the diagnostic a self-hoster needs ("connection refused",
                // "535 authentication failed"). SMTP servers never echo the password back, and host
                // and port are configuration rather than credentials — but the credentials
                // themselves must never appear here, so never interpolate the settings values.
                logger.LogError(ex, "Test email to user {UserId} failed", command.UserId);
                return new Response(false, $"Failed to send test email: {ex.Message}");
            }
        }
    }
}
