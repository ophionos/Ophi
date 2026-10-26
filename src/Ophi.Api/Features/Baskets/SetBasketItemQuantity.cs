using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Baskets;

public static class SetBasketItemQuantity
{
    public record Request(int Quantity);

    public record Command(Guid BasketId, Guid ProductId, int Quantity)
    {
        public Guid UserId { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.BasketId)
                .NotEmpty();

            RuleFor(x => x.ProductId)
                .NotEmpty();

            RuleFor(x => x.Quantity)
                .InclusiveBetween(1, AddBasketItems.MaxQuantity)
                .WithMessage($"Quantity must be between 1 and {AddBasketItems.MaxQuantity}");
        }
    }

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<BasketDto> Handle(Command request, CancellationToken cancellationToken)
        {
            var basket = await dbContext.Baskets
                .Include(b => b.Items)
                .FirstOrDefaultAsync(b => b.Id == request.BasketId && b.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Basket not found");

            if (basket.Items.All(i => i.ProductId != request.ProductId))
            {
                throw new NotFoundException("Product is not in this basket");
            }

            basket.SetQuantity(request.ProductId, request.Quantity);
            basket.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Quantity of product {ProductId} in basket {BasketId} set to {Quantity}",
                request.ProductId, basket.Id, request.Quantity);

            return await BasketReader.LoadAsync(dbContext, basket.Id, request.UserId, cancellationToken);
        }
    }

    public static void MapSetBasketItemQuantityEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPut("/api/v1/baskets/{id:guid}/items/{productId:guid}", async (Guid id, Guid productId, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, productId, request.Quantity)
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<BasketDto>(command);
            return Results.Ok(result);
        })
        .WithName("SetBasketItemQuantity")
        .WithTags("Baskets")
        .WithSummary("Set an item's quantity")
        .WithDescription("Sets the quantity (1 to 999) of a product already in the basket. Returns 404 when the basket does not exist, belongs to another user, or does not contain the product.")
        .Produces<BasketDto>(200)
        .RequireAuthorization();
}
