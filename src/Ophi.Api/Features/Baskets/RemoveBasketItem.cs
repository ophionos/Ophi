using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Baskets;

public static class RemoveBasketItem
{
    public record Command(Guid BasketId, Guid ProductId, Guid UserId);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var basket = await dbContext.Baskets
                .Include(b => b.Items)
                .FirstOrDefaultAsync(b => b.Id == request.BasketId && b.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Basket not found");

            if (!basket.RemoveItem(request.ProductId))
            {
                throw new NotFoundException("Product is not in this basket");
            }

            basket.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Product {ProductId} removed from basket {BasketId}", request.ProductId, basket.Id);
        }
    }

    public static void MapRemoveBasketItemEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapDelete("/api/v1/baskets/{id:guid}/items/{productId:guid}", async (Guid id, Guid productId, IMessageBus bus, HttpContext context) =>
        {
            await bus.InvokeAsync(new Command(id, productId, context.User.GetUserId()));
            return Results.NoContent();
        })
        .WithName("RemoveBasketItem")
        .WithTags("Baskets")
        .WithSummary("Remove a product from a basket")
        .WithDescription("Removes a product from a basket. The product itself is not deleted. Returns 404 when the basket does not exist, belongs to another user, or does not contain the product.")
        .Produces(204)
        .RequireAuthorization();
}
