using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Baskets;

public static class AddBasketItems
{
    public const int MaxProductsPerRequest = 200;
    public const int MaxQuantity = 999;

    public record Request(IReadOnlyList<Guid> ProductIds, int? Quantity = null);

    public record Command(Guid BasketId, IReadOnlyList<Guid> ProductIds, int? Quantity = null)
    {
        public Guid UserId { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.BasketId)
                .NotEmpty();

            RuleFor(x => x.ProductIds)
                .NotEmpty().WithMessage("At least one product is required")
                .Must(ids => ids.Count <= MaxProductsPerRequest)
                .WithMessage($"Cannot add more than {MaxProductsPerRequest} products at once");

            RuleForEach(x => x.ProductIds)
                .NotEmpty().WithMessage("Product IDs cannot be empty");

            RuleFor(x => x.Quantity)
                .InclusiveBetween(1, MaxQuantity)
                .When(x => x.Quantity != null);
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

            // All or nothing: one unknown or foreign product fails the whole request, so a client
            // cannot probe which ids exist and a partial add never has to be explained.
            var productIds = request.ProductIds.Distinct().ToList();
            var owned = await dbContext.Products
                .Where(p => p.UserId == request.UserId && productIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
            if (owned.Count != productIds.Count)
            {
                throw new NotFoundException("Product not found");
            }

            var quantity = request.Quantity ?? 1;
            var added = 0;
            foreach (var productId in productIds)
            {
                // AddItem returns the existing item untouched, so a product already in the basket keeps its quantity.
                var before = basket.Items.Count;
                basket.AddItem(productId, quantity);
                if (basket.Items.Count > before) added++;
            }

            if (added > 0)
            {
                basket.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            logger.LogInformation("Added {Count} product(s) to basket {BasketId}", added, basket.Id);

            return await BasketReader.LoadAsync(dbContext, basket.Id, request.UserId, cancellationToken);
        }
    }

    public static void MapAddBasketItemsEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/v1/baskets/{id:guid}/items", async (Guid id, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, request.ProductIds, request.Quantity)
            {
                UserId = context.User.GetUserId()
            };

            var result = await bus.InvokeAsync<BasketDto>(command);
            return Results.Ok(result);
        })
        .WithName("AddBasketItems")
        .WithTags("Baskets")
        .WithSummary("Add products to a basket")
        .WithDescription("Adds one or more of the user's products to a basket with the given quantity (default 1, at most 999). A product already in the basket is left as it is, quantity included. Returns 404 when the basket or any product does not exist or belongs to another user; nothing is added then.")
        .Produces<BasketDto>(200)
        .RequireAuthorization();
}
