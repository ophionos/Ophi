using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Exceptions;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Settings;
using Wolverine;

namespace Ophi.Api.Features.Auth;

public static class Register
{
    public record Command(string Email, string Password, string Name);

    // SecurityStamp is endpoint-internal (feeds the session cookie) and never serialized.
    public record Response(Guid Id, string Email, string Name, [property: JsonIgnore] string SecurityStamp);

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress()
                .MaximumLength(256);

            RuleFor(x => x.Password)
                .NotEmpty()
                .MinimumLength(8)
                .MaximumLength(128)
                .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter")
                .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter")
                .Matches("[0-9]").WithMessage("Password must contain at least one digit");

            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(100);
        }
    }

    public class Handler(OphiDbContext dbContext, IPasswordHasher<User> passwordHasher, IOptions<RegistrationSettings> registrationSettings, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            // Checked before the duplicate-email lookup so a closed instance reveals nothing about
            // which emails have accounts.
            if (!await GetRegistrationStatus.IsOpenAsync(dbContext, registrationSettings.Value, cancellationToken))
            {
                logger.LogWarning("Registration attempt while registration is disabled");
                throw new ApiException("Sign-up is closed on this server. Ask its owner for an account.", 403, "RegistrationDisabled");
            }

            var emailLower = request.Email.ToLowerInvariant();
            var existingUser = await dbContext.Users
                .FirstOrDefaultAsync(u => u.Email == emailLower, cancellationToken);

            if (existingUser != null)
            {
                logger.LogWarning("Registration attempt with existing email");
                throw new ApiException("Unable to create account", 422, "RegistrationFailed");
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = request.Email.ToLowerInvariant(),
                Name = request.Name
            };

            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("New user registered: {UserId}", user.Id);
            return new Response(user.Id, user.Email, user.Name, user.SecurityStamp);
        }
    }

    public static void MapRegisterEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/v1/auth/register", async (Command command, IMessageBus bus, HttpContext context) =>
        {
            var result = await bus.InvokeAsync<Response>(command);
            await context.SignInUserAsync(result.Id, result.Email, result.Name, result.SecurityStamp);
            return Results.Created($"/api/v1/users/{result.Id}", result);
        })
        .WithName("Register")
        .WithTags("Auth")
        .WithSummary("Register a new user account")
        .WithDescription("Creates a new user account and automatically signs in. Password must contain at least 8 characters with uppercase, lowercase, and a number. Rate-limited to prevent abuse. Returns 403 RegistrationDisabled when the operator has closed sign-up (see GET /api/v1/auth/registration).")
        .Produces<Register.Response>(201)
        .AllowAnonymous()
        .RequireRateLimiting("auth");
}
