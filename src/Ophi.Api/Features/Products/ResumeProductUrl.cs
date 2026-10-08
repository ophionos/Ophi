using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Products;

public static class ResumeProductUrl
{
    public static void MapResumeProductUrlEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/products/{id:guid}/urls/{urlId:guid}/resume",
                async (Guid id, Guid urlId, IMessageBus bus, HttpContext context) =>
                {
                    var command = new Command(id, urlId, context.User.GetUserId());
                    await bus.InvokeAsync(command);
                    return Results.Ok();
                })
            .WithName("ResumeProductUrl")
            .WithTags("Products")
            .WithSummary("Resume a suspended product URL")
            .WithDescription("Reactivates a URL that was automatically suspended after repeated scrape failures. Clears the consecutive error count and sets the status back to active.")
            .Produces(200)
            .RequireAuthorization();
    }

    public record Command(Guid ProductId, Guid ProductUrlId, Guid UserId);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var product = await dbContext.Products
                .Include(p => p.ProductUrls)
                .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Product not found");

            var productUrl = product.ProductUrls.FirstOrDefault(pu => pu.Id == request.ProductUrlId)
                ?? throw new NotFoundException("Product URL not found");

            productUrl.Resume();
            product.RecomputePriceAnomaly();
            // The dispatcher scans only Active products: without this a URL resumed on an errored
            // product is never scraped again. A product the user paused stays paused. The URL's
            // price rejoins the product MIN on its next scrape, not from its stale pre-pause value.
            if (product.Status == ProductStatus.Error)
                product.MarkActive();

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("URL {ProductUrlId} resumed on product {ProductId}", request.ProductUrlId, request.ProductId);
        }
    }
}
