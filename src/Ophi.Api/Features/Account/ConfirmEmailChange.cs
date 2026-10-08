using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Auth;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Account;

public static class ConfirmEmailChange
{
    public record Command(string Token);

    // SecurityStamp is endpoint-internal (feeds the re-issued session cookie) and never serialized.
    public record Response(Guid Id, string Email, string Name, [property: JsonIgnore] string SecurityStamp);

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Token)
                .NotEmpty();
        }
    }

    public class Handler(OphiDbContext dbContext, TimeProvider timeProvider, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var hashedToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));
            var now = timeProvider.GetUtcNow().UtcDateTime;

            var user = await dbContext.Users
                .FirstOrDefaultAsync(u =>
                    u.EmailChangeTokenHash == hashedToken &&
                    u.EmailChangeTokenExpiresAt > now &&
                    u.PendingEmail != null,
                    cancellationToken);

            if (user == null)
            {
                logger.LogWarning("Email change confirmation attempted with invalid or expired token");
                throw new ApiException("Invalid or expired confirmation link", 400, "InvalidEmailChangeToken");
            }

            // Someone may have registered the address since the change was requested.
            var emailTaken = await dbContext.Users
                .AnyAsync(u => u.Email == user.PendingEmail && u.Id != user.Id, cancellationToken);
            if (emailTaken)
            {
                throw new ApiException("This email is already in use", 409, "EmailInUse");
            }

            user.ConfirmEmailChange();
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The unique index on Email caught a registration that raced the check above.
                throw new ApiException("This email is already in use", 409, "EmailInUse");
            }

            logger.LogInformation("User {UserId} confirmed an email change", user.Id);
            return new Response(user.Id, user.Email, user.Name, user.SecurityStamp);
        }
    }

    public static void MapConfirmEmailChangeEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/v1/account/email/confirm", async (Command command, IMessageBus bus, HttpContext context) =>
        {
            var result = await bus.InvokeAsync<Response>(command);

            // The email lives in the cookie claims. Re-issue it when this browser is signed in as the
            // same user; any other caller (often a different browser) just signs in with the new email.
            if (context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value == result.Id.ToString())
            {
                await context.SignInUserAsync(result.Id, result.Email, result.Name, result.SecurityStamp);
            }

            return Results.Ok(new { email = result.Email });
        })
        .WithName("ConfirmEmailChange")
        .WithTags("Account")
        .WithSummary("Confirm an email change")
        .WithDescription("Completes an email change with the token from the confirmation link (valid 24 hours). The token alone authorizes it, so the link works in any browser. Returns 409 if the address was registered by another account in the meantime. Sessions stay signed in; a session of the same user gets its cookie refreshed.")
        .Produces(200)
        .AllowAnonymous()
        .RequireRateLimiting("auth");
}
