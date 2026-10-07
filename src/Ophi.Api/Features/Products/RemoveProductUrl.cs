using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Enums;
using Ophi.Domain.Services;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Products;

public static class RemoveProductUrl
{
    public static void MapRemoveProductUrlEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapDelete("/api/v1/products/{id:guid}/urls/{urlId:guid}", async (Guid id, Guid urlId, IMessageBus bus, HttpContext context) =>
        {
            var command = new Command(id, urlId, context.User.GetUserId());
            await bus.InvokeAsync(command);
            return Results.NoContent();
        })
        .WithName("RemoveProductUrl")
        .WithTags("Products")
        .WithSummary("Remove a URL from a product")
        .WithDescription("Removes a tracking URL from a product. The last URL cannot be removed — delete the product instead.")
        .Produces(204)
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

            if (product.ProductUrls.Count <= 1)
            {
                throw new ApiException("Cannot remove the last URL from a product", 400, "Bad Request");
            }

            dbContext.ProductUrls.Remove(productUrl);

            // Recalculate through ProductPriceAggregator, which owns the "MIN across live URLs,
            // with Currency and PreviousPrice following the winner" invariant. Assigning
            // CurrentPrice directly here left Currency pointing at the removed URL's currency (a
            // USD listing rendering as "€50") and skipped the PreviousPrice capture the dashboard's
            // "% change" reads. Paused URLs are excluded for the same reason as in
            // CheckProductPriceHandler: the dispatcher never re-scrapes them, so their price is frozen.
            var remainingPrices = product.ProductUrls
                .Where(pu => pu.Id != request.ProductUrlId
                    && pu.Status != ProductUrlStatus.Paused
                    && pu.CurrentPrice.HasValue)
                .Select(pu => new ProductPriceAggregator.UrlPrice(pu.CurrentPrice!.Value, pu.Currency))
                .ToList();

            product.RecomputePriceAnomaly(excludingUrlId: request.ProductUrlId);

            if (remainingPrices.Count > 0)
            {
                ProductPriceAggregator.ApplyAggregate(product, remainingPrices);
            }
            else
            {
                // ApplyAggregate no-ops on an empty set (so a scrape that found nothing can't clobber
                // a good price). Here an empty set genuinely means no live URL has a price left.
                product.CurrentPrice = null;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("URL {ProductUrlId} removed from product {ProductId}", request.ProductUrlId, request.ProductId);
        }
    }
}
