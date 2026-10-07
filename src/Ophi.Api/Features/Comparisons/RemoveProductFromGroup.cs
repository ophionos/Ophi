using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Comparisons;

public static class RemoveProductFromGroup
{
    public record Command(Guid GroupId, Guid ProductId, Guid UserId);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            _ = await dbContext.ComparisonGroups
                    .FirstOrDefaultAsync(cg => cg.Id == request.GroupId && cg.UserId == request.UserId, cancellationToken) ??
                throw new NotFoundException("Comparison group not found");

            var product = await dbContext.Products
                .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.UserId == request.UserId, cancellationToken) ??
                throw new NotFoundException("Product not found");

            if (product.ComparisonGroupId != request.GroupId)
            {
                throw new ApiException("Product is not in this comparison group");
            }

            product.ComparisonGroupId = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Product {ProductId} removed from comparison group {GroupId}", request.ProductId, request.GroupId);
        }
    }

    public static void MapRemoveProductFromGroupEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapDelete("/api/v1/comparisons/{groupId:guid}/products/{productId:guid}", async (Guid groupId, Guid productId, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(groupId, productId, context.User.GetUserId());
            await bus.InvokeAsync(command);
            return Results.NoContent();
        })
        .WithName("RemoveProductFromGroup")
        .WithTags("Comparisons")
        .WithSummary("Remove a product from a group")
        .WithDescription("Removes a product from a comparison group. The product is not deleted, only disassociated from the group.")
        .Produces(204)
        .RequireAuthorization();
}
