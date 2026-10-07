using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Tags;

public static class AddTagToProduct
{
    public static void MapAddTagToProductEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/products/{productId:guid}/tags", async (Guid productId, Request request, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(productId, request.TagId, context.User.GetUserId());
            await bus.InvokeAsync(command);
            return Results.NoContent();
        })
        .WithName("AddTagToProduct")
        .WithTags("Tags")
        .WithSummary("Add a tag to a product")
        .WithDescription("Associates an existing tag with a product. A product can have multiple tags. Adding a tag that is already assigned is a no-op.")
        .Produces(204)
        .RequireAuthorization();
    }

    public record Request(Guid TagId);

    public record Command(Guid ProductId, Guid TagId, Guid UserId);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var product = await dbContext.Products
                .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Product not found");

            var tag = await dbContext.Tags
                .FirstOrDefaultAsync(t => t.Id == request.TagId && t.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Tag not found");

            var existingProductTag = await dbContext.ProductTags
                .FirstOrDefaultAsync(pt => pt.ProductId == request.ProductId && pt.TagId == request.TagId, cancellationToken);

            if (existingProductTag != null)
            {
                throw new ApiException("Product already has this tag", 409, "Conflict");
            }

            var productTag = new ProductTag
            {
                ProductId = product.Id,
                TagId = tag.Id
            };

            dbContext.ProductTags.Add(productTag);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Tag {TagId} added to product {ProductId}", request.TagId, request.ProductId);
        }
    }
}
