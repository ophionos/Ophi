using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Tags;

public static class RemoveTagFromProduct
{
    public static void MapRemoveTagFromProductEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapDelete("/api/v1/products/{productId:guid}/tags/{tagId:guid}", async (Guid productId, Guid tagId, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(productId, tagId, context.User.GetUserId());
            await bus.InvokeAsync(command);
            return Results.NoContent();
        })
        .WithName("RemoveTagFromProduct")
        .WithTags("Tags")
        .WithSummary("Remove a tag from a product")
        .WithDescription("Removes a tag association from a product. The tag itself is not deleted.")
        .Produces(204)
        .RequireAuthorization();
    }

    public record Command(Guid ProductId, Guid TagId, Guid UserId);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            // Verify both product and tag belong to user
            var productExists = await dbContext.Products
                .AnyAsync(p => p.Id == request.ProductId && p.UserId == request.UserId, cancellationToken);

            if (!productExists)
            {
                throw new NotFoundException("Product not found");
            }

            var tagExists = await dbContext.Tags
                .AnyAsync(t => t.Id == request.TagId && t.UserId == request.UserId, cancellationToken);

            if (!tagExists)
            {
                throw new NotFoundException("Tag not found");
            }

            var productTag = await dbContext.ProductTags
                .FirstOrDefaultAsync(pt => pt.ProductId == request.ProductId && pt.TagId == request.TagId, cancellationToken)
                ?? throw new NotFoundException("Tag not found on product");

            dbContext.ProductTags.Remove(productTag);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Tag {TagId} removed from product {ProductId}", request.TagId, request.ProductId);
        }
    }
}
