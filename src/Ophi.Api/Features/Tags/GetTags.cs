using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Common.Helpers;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Tags;

public static class GetTags
{
    public static void MapGetTagsEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/tags", async (IMessageBus bus, HttpContext context, [Microsoft.AspNetCore.Mvc.FromQuery] string? search = null) =>
        {
            var query = new Query(context.User.GetUserId(), search);
            var result = await bus.InvokeAsync<Response>(query);
            return Results.Ok(result);
        })
        .WithName("GetTags")
        .WithTags("Tags")
        .WithSummary("List tags")
        .WithDescription("Returns all tags for the current user with product counts. Supports optional search filtering by name.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Query(Guid UserId, string? Search = null);

    public record Response(List<TagDto> Items);

    public record TagDto(Guid Id, string Name, string Color, int Weight, int ProductCount);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching tags for user {UserId}", request.UserId);

            // lower()+LIKE for case-insensitive search on both SQLite and Postgres (ILike is Npgsql-only).
            // LikePattern escapes %/_ so literal wildcards match literally; the 3-arg Like emits ESCAPE.
            var search = request.Search == null ? null : LikePattern.Contains(request.Search);
            var tags = await dbContext.Tags
                .Where(t => t.UserId == request.UserId &&
                    (search == null || EF.Functions.Like(t.Name.ToLower(), search, LikePattern.EscapeChar)))
                .OrderByDescending(t => t.Weight)
                .ThenBy(t => t.Name)
                .Select(t => new TagDto(
                    t.Id,
                    t.Name,
                    t.Color,
                    t.Weight,
                    t.ProductTags.Count
                ))
                .ToListAsync(cancellationToken);

            logger.LogDebug("Returning {Count} tags for user {UserId}", tags.Count, request.UserId);
            return new Response(tags);
        }
    }
}
