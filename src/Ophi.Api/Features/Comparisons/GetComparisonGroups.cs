using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Comparisons;

public static class GetComparisonGroups
{
    public record Query(Guid UserId);

    public record Response(List<Dto> Items);

    public record Dto(Guid Id, string Name, int ProductCount);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching comparison groups for user {UserId}", request.UserId);

            var groups = await dbContext.ComparisonGroups
                .Where(cg => cg.UserId == request.UserId)
                .OrderBy(cg => cg.Name)
                .Select(cg => new Dto(
                    cg.Id,
                    cg.Name,
                    cg.Products.Count
                ))
                .ToListAsync(cancellationToken);

            logger.LogDebug("Returning {Count} comparison groups for user {UserId}", groups.Count, request.UserId);
            return new Response(groups);
        }
    }

    public static void MapGetComparisonGroupsEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/v1/comparisons", async (IMessageBus bus, HttpContext context) =>
        {
            var query = new Query(context.User.GetUserId());
            var result = await bus.InvokeAsync<Response>(query);
            return Results.Ok(result);
        })
        .WithName("GetComparisonGroups")
        .WithTags("Comparisons")
        .WithSummary("List comparison groups")
        .WithDescription("Returns all comparison groups for the current user with the product count of each group. Get a single group for its prices.")
        .Produces<Response>(200)
        .RequireAuthorization();
}
