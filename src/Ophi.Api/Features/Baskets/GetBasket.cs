using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Baskets;

public static class GetBasket
{
    public record Query(Guid Id, Guid UserId);

    public class Handler(OphiDbContext dbContext)
    {
        public Task<BasketDto> Handle(Query request, CancellationToken cancellationToken) =>
            BasketReader.LoadAsync(dbContext, request.Id, request.UserId, cancellationToken);
    }

    public static void MapGetBasketEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/v1/baskets/{id:guid}", async (Guid id, IMessageBus bus, HttpContext context) =>
        {
            var result = await bus.InvokeAsync<BasketDto>(new Query(id, context.User.GetUserId()));
            return Results.Ok(result);
        })
        .WithName("GetBasket")
        .WithTags("Baskets")
        .WithSummary("Get a basket")
        .WithDescription("Returns one basket with its items (product name, image and quantity), in the order they were added, and its totals. `stores`: one entry per site (normalized host) that sells at least one item through a URL that is not paused, not out of stock and priced; each item at its cheapest price there, the subtotal (price x quantity), shipping once from the user's rate for that site (a free-above threshold applies to the subtotal), the total, and `complete`/`missingCount`. A site whose prices are in different currencies has null currency, subtotal and total; a rate in another currency makes shipping and total null. Complete sites come first, cheapest first. `bestStore` is the cheapest complete site inside the dominant currency of those totals. `split` buys each item at its cheapest site in the basket's dominant currency, with shipping once per site used; no currency conversion anywhere. Returns 404 for a basket that belongs to another user.")
        .Produces<BasketDto>(200)
        .RequireAuthorization();
}
