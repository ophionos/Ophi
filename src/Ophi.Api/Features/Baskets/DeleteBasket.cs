using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Baskets;

public static class DeleteBasket
{
    public record Command(Guid Id, Guid UserId);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            // Items go with the basket through the FK cascade.
            var basket = await dbContext.Baskets
                .FirstOrDefaultAsync(b => b.Id == request.Id && b.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Basket not found");

            dbContext.Baskets.Remove(basket);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Basket {BasketId} deleted", request.Id);
        }
    }

    public static void MapDeleteBasketEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapDelete("/api/v1/baskets/{id:guid}", async (Guid id, IMessageBus bus, HttpContext context) =>
        {
            await bus.InvokeAsync(new Command(id, context.User.GetUserId()));
            return Results.NoContent();
        })
        .WithName("DeleteBasket")
        .WithTags("Baskets")
        .WithSummary("Delete a basket")
        .WithDescription("Permanently deletes a basket and its items. The products themselves are not deleted. Returns 404 for a basket that belongs to another user.")
        .Produces(204)
        .RequireAuthorization();
}
