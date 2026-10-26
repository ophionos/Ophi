using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Baskets;

public static class GetBaskets
{
    public record Query(Guid UserId);

    public record Response(List<BasketSummaryDto> Items);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching baskets for user {UserId}", request.UserId);

            var items = await dbContext.Baskets
                .AsNoTracking()
                .Where(b => b.UserId == request.UserId)
                .OrderByDescending(b => b.UpdatedAt)
                .ThenBy(b => b.Name)
                .Select(b => new BasketSummaryDto(b.Id, b.Name, b.Description, b.Items.Count, b.UpdatedAt))
                .ToListAsync(cancellationToken);

            return new Response(items);
        }
    }

    public static void MapGetBasketsEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/v1/baskets", async (IMessageBus bus, HttpContext context) =>
        {
            var result = await bus.InvokeAsync<Response>(new Query(context.User.GetUserId()));
            return Results.Ok(result);
        })
        .WithName("GetBaskets")
        .WithTags("Baskets")
        .WithSummary("List baskets")
        .WithDescription("Returns the current user's baskets with the number of items in each, most recently updated first. Get a single basket for its items.")
        .Produces<Response>(200)
        .RequireAuthorization();
}
