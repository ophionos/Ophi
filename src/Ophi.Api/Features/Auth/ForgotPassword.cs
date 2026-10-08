using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Email;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Auth;

public static class ForgotPassword
{
    public record Command(string Email);

    public record Response;

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress();
        }
    }

    /// <summary>
    /// Background message carrying the normalized email. The lookup, token write and send happen in
    /// <see cref="SendResetEmailHandler"/>, off the request path.
    /// </summary>
    public record SendResetEmail(string Email);

    // Does the same work for every email — no lookup, no token write, no send — so the response time
    // cannot reveal whether an account exists. Handled in the API process (the Worker does not discover
    // this assembly, and the link needs APP_URL).
    public class Handler(IMessageBus bus)
    {
        public async Task<Response> Handle(Command request)
        {
            await bus.PublishAsync(new SendResetEmail(request.Email.ToLowerInvariant()));
            return new Response();
        }
    }

    public class SendResetEmailHandler(OphiDbContext dbContext, IEmailService emailService, TimeProvider timeProvider, ILogger<SendResetEmailHandler> logger)
    {
        public async Task Handle(SendResetEmail message, CancellationToken cancellationToken)
        {
            var email = message.Email.ToLowerInvariant();
            var user = await dbContext.Users
                .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

            if (user == null)
            {
                logger.LogDebug("Password reset requested for unknown email");
                return;
            }

            var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var hashedToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

            user.PasswordResetTokenHash = hashedToken;
            user.PasswordResetTokenExpiresAt = timeProvider.GetUtcNow().UtcDateTime.AddHours(1);
            await dbContext.SaveChangesAsync(cancellationToken);

            await emailService.SendPasswordResetAsync(user.Email, rawToken, cancellationToken);

            logger.LogInformation("Password reset token generated for user {UserId}", user.Id);
        }
    }

    public static void MapForgotPasswordEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/v1/auth/forgot-password", async (Command command, IMessageBus bus) =>
        {
            await bus.InvokeAsync<Response>(command);
            return Results.Ok(new { message = "If an account with that email exists, a reset link has been sent." });
        })
        .WithName("ForgotPassword")
        .WithTags("Auth")
        .WithSummary("Request a password reset email")
        .WithDescription("Sends a password reset link to the provided email address. Always returns 200 regardless of whether the email exists to prevent email enumeration. Reset tokens expire after 1 hour.")
        .Produces(200)
        .AllowAnonymous()
        .RequireRateLimiting("auth");
}
