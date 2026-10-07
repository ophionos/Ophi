using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Comparisons;

public static class AddProductsToGroup
{
    public const int MaxProductsPerRequest = 200;

    public record Request(IReadOnlyList<Guid> ProductIds);

    public record Command(Guid GroupId, IReadOnlyList<Guid> ProductIds)
    {
        public Guid UserId { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.GroupId)
                .NotEmpty();

            RuleFor(x => x.ProductIds)
                .NotEmpty().WithMessage("At least one product is required")
                .Must(ids => ids.Count <= MaxProductsPerRequest)
                .WithMessage($"Cannot add more than {MaxProductsPerRequest} products at once");

            RuleForEach(x => x.ProductIds)
                .NotEmpty().WithMessage("Product IDs cannot be empty");
        }
    }

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            // Sequential awaits: EF Core's DbContext does not allow concurrent operations on the
            // same scoped instance.
            _ = await dbContext.ComparisonGroups
                .FirstOrDefaultAsync(cg => cg.Id == request.GroupId && cg.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Comparison group not found");

            // Only products owned by the user are loaded — foreign/unknown ids are silently ignored.
            // Reassigning ComparisonGroupId moves a product already in another group into this one.
            var products = await dbContext.Products
                .Where(p => p.UserId == request.UserId && request.ProductIds.Contains(p.Id))
                .ToListAsync(cancellationToken);

            foreach (var product in products)
            {
                product.ComparisonGroupId = request.GroupId;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Added {Count} product(s) to comparison group {GroupId}",
                products.Count, request.GroupId);
        }
    }

    public static void MapAddProductsToGroupEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/v1/comparisons/{id:guid}/products/batch", async (Guid id, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, request.ProductIds)
            {
                UserId = context.User.GetUserId()
            };

            await bus.InvokeAsync(command);
            return Results.NoContent();
        })
        .WithName("AddProductsToGroup")
        .WithTags("Comparisons")
        .WithSummary("Add multiple products to a group")
        .WithDescription("Adds one or more existing products to a comparison group in a single request. Products already in another group are moved. Unknown or non-owned product IDs are ignored.")
        .Produces(204)
        .RequireAuthorization();
}
