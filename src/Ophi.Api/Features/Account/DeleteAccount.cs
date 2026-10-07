using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Auth;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Account;

public static class DeleteAccount
{
    public record Request(string Password);

    public record Command(string Password)
    {
        public Guid UserId { get; init; }
    }

    public record Response;

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Password)
                .NotEmpty();
        }
    }

    public class Handler(OphiDbContext dbContext, IPasswordHasher<User> passwordHasher, SecurityStampGuard stampGuard, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var user = await dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
                ?? throw new UnauthorizedException();

            var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (result == PasswordVerificationResult.Failed)
            {
                logger.LogWarning("Account deletion failed: wrong password for user {UserId}", user.Id);
                throw new UnauthorizedException("Password is incorrect");
            }

            // Hard delete; products, alerts, notifications, tags, comparison groups,
            // store configs, webhook targets, and API keys all cascade via FK.
            dbContext.Users.Remove(user);
            await dbContext.SaveChangesAsync(cancellationToken);
            stampGuard.Evict(user.Id);

            logger.LogInformation("User {UserId} deleted their account", user.Id);
            return new Response();
        }
    }

    public static void MapDeleteAccountEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapDelete("/api/v1/account", async ([FromBody] Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(request.Password)
            {
                UserId = context.User.GetUserId()
            };

            await bus.InvokeAsync<Response>(command);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        })
        .WithName("DeleteAccount")
        .WithTags("Account")
        .WithSummary("Delete account")
        .WithDescription("Permanently deletes the signed-in user's account and all owned data (products, alerts, notifications, tags, comparison groups, store configs, webhooks, API keys). Requires the account password. This cannot be undone.")
        .Produces(204)
        .RequireAuthorization()
        .RequireRateLimiting("auth");
}
