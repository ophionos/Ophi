using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ophi.Api.Common.Exceptions;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Auth;

public static class Login
{
    public record Command(string Email, string Password);

    // SecurityStamp is endpoint-internal (feeds the session cookie) and never serialized.
    public record Response(Guid Id, string Email, string Name, [property: JsonIgnore] string SecurityStamp);

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress();

            RuleFor(x => x.Password)
                .NotEmpty();
        }
    }

    public class Handler(
        OphiDbContext dbContext,
        IPasswordHasher<User> passwordHasher,
        IOptions<PasswordHasherOptions> hasherOptions,
        TimeProvider timeProvider,
        ILogger<Handler> logger)
    {
        // Decoy credentials for the unknown-email path. Hashed once per process with the same
        // algorithm the real users use, so the verify below does representative work.
        private static readonly User TimingDecoyUser = new() { Email = string.Empty, Name = string.Empty, PasswordHash = string.Empty };

        // Built from the application's configured PasswordHasherOptions, NOT a bare
        // `new PasswordHasher<User>()`. PBKDF2 verification reads its iteration count out of the hash
        // itself, so a decoy hashed at default cost would keep verifying at default cost even after
        // the options are tuned — silently reopening the timing gap this decoy exists to close.
        // Deriving it from the options rather than from the injected hasher also keeps this static
        // cache honest: a test that supplies a mock `IPasswordHasher` must not be able to poison a
        // process-wide hash that the real hasher will later be asked to parse.
        private static string? _timingDecoyHash;

        private string TimingDecoyHash =>
            _timingDecoyHash ??= new PasswordHasher<User>(hasherOptions)
                .HashPassword(TimingDecoyUser, "timing-equalization-decoy");


        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var email = request.Email.ToLowerInvariant();
            var user = await dbContext.Users
                .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

            if (user == null)
            {
                // Verify against a throwaway hash so an unknown email costs the same PBKDF2 work as
                // a known one. The error message is already identical for both cases, but without
                // this the ~100ms hashing difference still leaks whether an account exists.
                passwordHasher.VerifyHashedPassword(TimingDecoyUser, TimingDecoyHash, request.Password);
                logger.LogWarning("Login failed: unknown email {Email}", email);
                throw new UnauthorizedException("Invalid email or password");
            }

            var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (result == PasswordVerificationResult.Failed)
            {
                logger.LogWarning("Login failed: wrong password for user {UserId}", user.Id);
                throw new UnauthorizedException("Invalid email or password");
            }

            user.LastLoginAt = timeProvider.GetUtcNow().UtcDateTime;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("User {UserId} logged in", user.Id);
            return new Response(user.Id, user.Email, user.Name, user.SecurityStamp);
        }
    }

    public static void MapLoginEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/v1/auth/login", async (Command command, IMessageBus bus, HttpContext context) =>
        {
            var result = await bus.InvokeAsync<Response>(command);
            await context.SignInUserAsync(result.Id, result.Email, result.Name, result.SecurityStamp);
            return Results.Ok(result);
        })
        .WithName("Login")
        .WithTags("Auth")
        .WithSummary("Log in with email and password")
        .WithDescription("Authenticates a user with email and password credentials. Sets an HTTP-only authentication cookie on success. Rate-limited to prevent brute force attacks.")
        .Produces<Login.Response>(200)
        .AllowAnonymous()
        .RequireRateLimiting("auth");
}
