using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.ApiKeys;

public static class ListApiKeys
{
    public static void MapListApiKeysEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/api-keys", async (IMessageBus bus, HttpContext context) =>
        {
            var query = new Query { UserId = context.User.GetUserId() };
            var result = await bus.InvokeAsync<List<Response>>(query);
            return Results.Ok(result);
        })
        .WithName("ListApiKeys")
        .WithTags("ApiKeys")
        .WithSummary("List API keys")
        .WithDescription("Returns all API keys for the current user. Key values are never returned — only metadata.")
        .Produces<List<Response>>(200)
        .RequireAuthorization();
    }

    public record Query
    {
        public Guid UserId { get; init; }
    }

    public record Response(
        Guid Id,
        string Name,
        List<string> Scopes,
        DateTime? LastUsedAt,
        DateTime? ExpiresAt,
        DateTime CreatedAt);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<List<Response>> Handle(Query request, CancellationToken cancellationToken)
        {
            var keys = await dbContext.ApiKeys
                .Where(k => k.UserId == request.UserId)
                .OrderByDescending(k => k.CreatedAt)
                .Select(k => new Response(k.Id, k.Name, k.Scopes, k.LastUsedAt, k.ExpiresAt, k.CreatedAt))
                .ToListAsync(cancellationToken);

            logger.LogDebug("Returning {Count} API keys for user {UserId}", keys.Count, request.UserId);
            return keys;
        }
    }
}
