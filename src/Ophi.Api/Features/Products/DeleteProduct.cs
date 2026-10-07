using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Products;

public static class DeleteProduct
{
    public static void MapDeleteProductEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapDelete("/api/v1/products/{id:guid}", async (Guid id, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, context.User.GetUserId());
            await bus.InvokeAsync(command);
            return Results.NoContent();
        })
        .WithName("DeleteProduct")
        .WithTags("Products")
        .WithSummary("Delete a product")
        .WithDescription("Permanently deletes a product and all associated data including price history, URLs, alerts, and notifications.")
        .Produces(204)
        .RequireAuthorization();
    }

    public record Command(Guid ProductId, Guid UserId);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var product = await dbContext.Products
                .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.UserId == request.UserId, cancellationToken) ?? throw new NotFoundException("Product not found");
            dbContext.Products.Remove(product);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Product {ProductId} deleted by user {UserId}", request.ProductId, request.UserId);
        }
    }
}
