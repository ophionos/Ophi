using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Auth;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Features.Auth;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Account;

public static class ChangePassword
{
    public record Request(string CurrentPassword, string NewPassword);

    public record Command(string CurrentPassword, string NewPassword)
    {
        public Guid UserId { get; init; }
    }

    // SecurityStamp is endpoint-internal (feeds the re-issued session cookie) and never serialized.
    public record Response(Guid Id, string Email, string Name, [property: JsonIgnore] string SecurityStamp);

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.CurrentPassword)
                .NotEmpty();

            RuleFor(x => x.NewPassword)
                .NotEmpty()
                .MinimumLength(8)
                .MaximumLength(128)
                .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter")
                .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter")
                .Matches("[0-9]").WithMessage("Password must contain at least one digit");
        }
    }

    public class Handler(OphiDbContext dbContext, IPasswordHasher<User> passwordHasher, SecurityStampGuard stampGuard, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var user = await dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
                ?? throw new UnauthorizedException();

            var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
            if (result == PasswordVerificationResult.Failed)
            {
                logger.LogWarning("Password change failed: wrong current password for user {UserId}", user.Id);
                throw new UnauthorizedException("Current password is incorrect");
            }

            user.ChangePassword(passwordHasher.HashPassword(user, request.NewPassword));
            await dbContext.SaveChangesAsync(cancellationToken);
            stampGuard.Refresh(user.Id, user.SecurityStamp);

            logger.LogInformation("User {UserId} changed their password", user.Id);
            return new Response(user.Id, user.Email, user.Name, user.SecurityStamp);
        }
    }

    public static void MapChangePasswordEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPut("/api/v1/account/password", async (Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(request.CurrentPassword, request.NewPassword)
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<Response>(command);

            // The stamp rotation just invalidated every session ticket, including this one —
            // re-issue the cookie so the caller stays signed in.
            await context.SignInUserAsync(result.Id, result.Email, result.Name, result.SecurityStamp);
            return Results.Ok(new { message = "Password changed. Other sessions have been signed out." });
        })
        .WithName("ChangePassword")
        .WithTags("Account")
        .WithSummary("Change password")
        .WithDescription("Changes the signed-in user's password. Requires the current password. All other sessions are signed out; the current session receives a fresh cookie. The same password requirements as registration apply.")
        .Produces(200)
        .RequireAuthorization()
        .RequireRateLimiting("auth");
}
