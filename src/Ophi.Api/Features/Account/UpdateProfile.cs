using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Features.Auth;
using Ophi.Domain.Entities;
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
    public record Response(Guid Id, string Email, string Name, [property: JsonIgnore] string SecurityStamp);

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

    public class Handler(OphiDbContext dbContext, IPasswordHasher<User> passwordHasher, ILogger<Handler> logger)
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

            user.UpdateProfile(request.Name, request.Email);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("User {UserId} updated their profile", user.Id);
            return new Response(user.Id, user.Email, user.Name, user.SecurityStamp);
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
        .WithDescription("Updates the signed-in user's display name and email. Changing the email requires the current password. Existing sessions stay signed in; the current session's cookie is refreshed with the new identity.")
        .Produces<Response>(200)
        .RequireAuthorization()
        .RequireRateLimiting("auth");
}
