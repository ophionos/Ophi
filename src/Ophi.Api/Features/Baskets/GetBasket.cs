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
        .WithDescription("Returns one basket with its items (product name, image and quantity), in the order they were added. Returns 404 for a basket that belongs to another user.")
        .Produces<BasketDto>(200)
        .RequireAuthorization();
}
