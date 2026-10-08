using System.Security.Cryptography;
using FluentValidation;
using Microsoft.AspNetCore.WebUtilities;
using Ophi.Api.Common.Auth;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.ApiKeys;

public static class CreateApiKey
{
    public static void MapCreateApiKeyEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/api-keys", async (Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(request.Name, request.Scopes, request.ExpiresAt)
            {
                UserId = context.User.GetUserId()
            };
            var result = await bus.InvokeAsync<Response>(command);
            return Results.Created($"/api/v1/api-keys/{result.Id}", result);
        })
        .WithName("CreateApiKey")
        .WithTags("ApiKeys")
        .WithSummary("Create an API key")
        .WithDescription("Creates a new API key. The raw key is returned exactly once in the response — it cannot be retrieved again.")
        .Produces<Response>(201)
        .RequireAuthorization(AuthPolicies.SessionOnly);
    }

    public record Request(string Name, List<string> Scopes, DateTime? ExpiresAt = null);

    public record Command(string Name, List<string> Scopes, DateTime? ExpiresAt)
    {
        public Guid UserId { get; init; }
    }

    public record Response(Guid Id, string Name, List<string> Scopes, string Key, DateTime? ExpiresAt, DateTime CreatedAt);

    public class Validator : AbstractValidator<Command>
    {
        private static readonly HashSet<string> ValidScopes = ["read", "write"];

        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Scopes).NotEmpty().WithMessage("At least one scope is required.");
            RuleForEach(x => x.Scopes).Must(s => ValidScopes.Contains(s))
                .WithMessage("Scope must be 'read' or 'write'.");
            RuleFor(x => x.ExpiresAt)
                .Must(d => !d.HasValue || d.Value > DateTime.UtcNow)
                .WithMessage("Expiration date must be in the future.");
        }
    }

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            // Generate 32-byte CSPRNG key → Base64URL
            var rawBytes = RandomNumberGenerator.GetBytes(32);
            var rawKey = $"ophi_{WebEncoders.Base64UrlEncode(rawBytes)}";
            var keyHash = ApiKeyAuthenticationHandler.HashKey(rawKey);

            var apiKey = new ApiKey
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                Name = request.Name,
                KeyHash = keyHash,
                Scopes = request.Scopes.Distinct().ToList(),
                ExpiresAt = request.ExpiresAt
            };

            dbContext.ApiKeys.Add(apiKey);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("API key {ApiKeyId} created for user {UserId} with scopes [{Scopes}]",
                apiKey.Id, request.UserId, string.Join(", ", apiKey.Scopes));

            return new Response(apiKey.Id, apiKey.Name, apiKey.Scopes, rawKey, apiKey.ExpiresAt, apiKey.CreatedAt);
        }
    }
}
