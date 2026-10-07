using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Comparisons;

public static class AddProductToGroup
{
    public record Request(Guid ProductId);

    public record Command(Guid GroupId, Guid ProductId)
    {
        public Guid UserId { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.GroupId)
                .NotEmpty();

            RuleFor(x => x.ProductId)
                .NotEmpty();
        }
    }

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            // Run sequentially: EF Core's DbContext does not allow concurrent operations on the
            // same instance, so these queries cannot share one scoped context via Task.WhenAll.
            _ = await dbContext.ComparisonGroups
                .FirstOrDefaultAsync(cg => cg.Id == request.GroupId && cg.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Comparison group not found");
            var product = await dbContext.Products
                .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Product not found");

            product.ComparisonGroupId = request.GroupId;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Product {ProductId} added to comparison group {GroupId}", request.ProductId, request.GroupId);
        }
    }

    public static void MapAddProductToGroupEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/v1/comparisons/{id:guid}/products", async (Guid id, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, request.ProductId)
            {
                UserId = context.User.GetUserId()
            };

            await bus.InvokeAsync(command);
            return Results.NoContent();
        })
        .WithName("AddProductToGroup")
        .WithTags("Comparisons")
        .WithSummary("Add a product to a group")
        .WithDescription("Adds an existing product to a comparison group. A product can belong to at most one comparison group at a time. If the product is already in another group, it is moved.")
        .Produces(204)
        .RequireAuthorization();
}
