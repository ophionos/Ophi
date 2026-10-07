using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Auth;
using Ophi.Api.Common.Exceptions;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Auth;

public static class ResetPassword
{
    public record Command(string Token, string NewPassword);

    public record Response;

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Token)
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

    public class Handler(OphiDbContext dbContext, IPasswordHasher<User> passwordHasher, TimeProvider timeProvider, SecurityStampGuard stampGuard, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            var hashedToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));
            var now = timeProvider.GetUtcNow().UtcDateTime;

            var user = await dbContext.Users
                .FirstOrDefaultAsync(u =>
                    u.PasswordResetTokenHash == hashedToken &&
                    u.PasswordResetTokenExpiresAt > now,
                    cancellationToken);

            if (user == null)
            {
                logger.LogWarning("Password reset attempted with invalid or expired token");
                throw new ApiException("Invalid or expired reset token", 400, "InvalidResetToken");
            }

            user.ChangePassword(passwordHasher.HashPassword(user, request.NewPassword));
            await dbContext.SaveChangesAsync(cancellationToken);
            stampGuard.Refresh(user.Id, user.SecurityStamp);

            logger.LogInformation("Password reset completed for user {UserId}", user.Id);
            return new Response();
        }
    }

    public static void MapResetPasswordEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/v1/auth/reset-password", async (Command command, IMessageBus bus) =>
        {
            await bus.InvokeAsync<Response>(command);
            return Results.Ok(new { message = "Password has been reset successfully." });
        })
        .WithName("ResetPassword")
        .WithTags("Auth")
        .WithSummary("Reset password with token")
        .WithDescription("Resets a user's password using the token from the forgot-password email. Token must be valid and not expired (1-hour window). The same password requirements as registration apply.")
        .Produces(200)
        .AllowAnonymous()
        .RequireRateLimiting("auth");
}
