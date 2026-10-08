using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Features.Auth;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Email;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Account;

public static class UpdateProfile
{
    public record Request(string Name, string Email, string? CurrentPassword);

    public record Command(string Name, string Email, string? CurrentPassword)
    {
        public Guid UserId { get; init; }
    }

    // SecurityStamp is endpoint-internal (feeds the re-issued session cookie) and never serialized.
    // PendingEmail is set while a verified change waits on its confirmation link.
    public record Response(Guid Id, string Email, string Name, string? PendingEmail, [property: JsonIgnore] string SecurityStamp);

    /// <summary>
    /// Background message for a verified email change. Carries no token: the handler issues it, so a
    /// raw token never sits in the durable envelope tables. <paramref name="PendingEmail"/> is the
    /// address this message was published for; a newer request makes the message stale.
    /// </summary>
    public record SendEmailChangeConfirmation(Guid UserId, string PendingEmail);

    public static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromHours(24);

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(100);

            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress()
                .MaximumLength(256);
        }
    }

    public class Handler(
        OphiDbContext dbContext,
        IPasswordHasher<User> passwordHasher,
        IOptions<EmailSettings> emailSettings,
        IMessageBus bus,
        ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var user = await dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
                ?? throw new UnauthorizedException();

            var newEmail = request.Email.ToLowerInvariant();
            if (newEmail != user.Email)
            {
                // Email is the login credential — changing it needs the current password,
                // so a hijacked session can't silently take over the account.
                if (string.IsNullOrEmpty(request.CurrentPassword))
                {
                    throw new ApiException("Current password is required to change email", 400, "CurrentPasswordRequired");
                }

                var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
                if (result == PasswordVerificationResult.Failed)
                {
                    logger.LogWarning("Email change failed: wrong current password for user {UserId}", user.Id);
                    throw new UnauthorizedException("Current password is incorrect");
                }

                var emailTaken = await dbContext.Users
                    .AnyAsync(u => u.Email == newEmail && u.Id != user.Id, cancellationToken);
                if (emailTaken)
                {
                    throw new ApiException("This email is already in use", 409, "EmailInUse");
                }
            }

            // With SMTP the new address must prove itself first. Without it no link can be sent, so the
            // password-gated change applies at once (password reset is unavailable then too).
            var verifyChange = newEmail != user.Email && emailSettings.Value.IsConfigured;
            if (verifyChange)
            {
                user.UpdateProfile(request.Name, user.Email);
                user.RequestEmailChange(newEmail);
            }
            else
            {
                user.UpdateProfile(request.Name, newEmail);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            if (verifyChange)
            {
                await bus.PublishAsync(new SendEmailChangeConfirmation(user.Id, newEmail));
                logger.LogInformation("User {UserId} requested an email change", user.Id);
            }

            logger.LogInformation("User {UserId} updated their profile", user.Id);
            return new Response(user.Id, user.Email, user.Name, user.PendingEmail, user.SecurityStamp);
        }
    }

    // Handled in the API process, like ForgotPassword.SendResetEmailHandler: the Worker does not
    // discover this assembly.
    public class SendEmailChangeConfirmationHandler(
        OphiDbContext dbContext,
        IEmailService emailService,
        TimeProvider timeProvider,
        ILogger<SendEmailChangeConfirmationHandler> logger)
    {
        public async Task Handle(SendEmailChangeConfirmation message, CancellationToken cancellationToken)
        {
            var user = await dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == message.UserId, cancellationToken);

            if (user is null || user.PendingEmail != message.PendingEmail)
            {
                logger.LogDebug("Email change confirmation for user {UserId} is stale; nothing sent", message.UserId);
                return;
            }

            var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var hashedToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
            user.IssueEmailChangeToken(hashedToken, timeProvider.GetUtcNow().UtcDateTime.Add(ConfirmationLifetime));
            await dbContext.SaveChangesAsync(cancellationToken);

            await emailService.SendEmailChangeNoticeAsync(user.Email, message.PendingEmail, cancellationToken);
            await emailService.SendEmailChangeConfirmationAsync(message.PendingEmail, rawToken, cancellationToken);

            logger.LogInformation("Email change confirmation sent for user {UserId}", user.Id);
        }
    }

    public static void MapUpdateProfileEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPut("/api/v1/account/profile", async (Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(request.Name, request.Email, request.CurrentPassword)
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<Response>(command);

            // Name and email live in the cookie claims (and /auth/me reads from them) —
            // re-issue the ticket so the session reflects the new identity immediately.
            await context.SignInUserAsync(result.Id, result.Email, result.Name, result.SecurityStamp);
            return Results.Ok(result);
        })
        .WithName("UpdateProfile")
        .WithTags("Account")
        .WithSummary("Update profile")
        .WithDescription("Updates the signed-in user's display name and email. Changing the email requires the current password. When the server has SMTP, the email does not change yet: `pendingEmail` is set, a confirmation link (valid 24 hours) goes to the new address, and a notice goes to the current one; `POST /account/email/confirm` completes the change. Without SMTP the email changes at once. Existing sessions stay signed in; the current session's cookie is refreshed with the new identity.")
        .Produces<Response>(200)
        .RequireAuthorization()
        .RequireRateLimiting("auth");
}
