using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Enums;
using Ophi.Domain.Messages.Commands;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Products;

public static class RetryScrapeProductUrl
{
    public static void MapRetryScrapeProductUrlEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/products/{id:guid}/urls/{urlId:guid}/retry",
                async (Guid id, Guid urlId, IMessageBus bus, HttpContext context) =>
                {
                    var command = new Command(id, urlId, context.User.GetUserId());
                    await bus.InvokeAsync(command);
                    return Results.Accepted();
                })
            .WithName("RetryScrapeProductUrl")
            .WithTags("Products")
            .WithSummary("Retry scraping a product URL")
            .WithDescription("Queues a URL for re-scraping. Useful when the initial scrape failed or returned incorrect data. Resets the URL error state and processes asynchronously.")
            .Produces(202)
            .RequireAuthorization();
    }

    public record Command(Guid ProductId, Guid ProductUrlId, Guid UserId);

    public class Handler(OphiDbContext dbContext, IMessageBus messageBus, ILogger<Handler> logger)
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var product = await dbContext.Products
                .Include(p => p.ProductUrls)
                .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.UserId == request.UserId, cancellationToken)
                ?? throw new NotFoundException("Product not found");

            var productUrl = product.ProductUrls.FirstOrDefault(pu => pu.Id == request.ProductUrlId)
                ?? throw new NotFoundException("Product URL not found");

            // Persist the retry intent first: clear the URL's error/failure budget and mark it due now,
            // and reactivate an errored/paused product so the worker's PriceCheckDispatcher (which only
            // scans Active products) re-scrapes it. This DB state is the source of truth and the
            // reconciliation backstop if the publish below is ever lost.
            productUrl.RequestImmediateRescrape();
            product.RecomputePriceAnomaly(); // RequestImmediateRescrape resumes the URL, clearing its anomaly flag.
            if (product.Status is ProductStatus.Error or ProductStatus.Paused)
                product.MarkActive();

            await dbContext.SaveChangesAsync(cancellationToken);

            // Instant trigger: publish a forced scrape over the durable Postgres transport (split mode)
            // or in-process (embedded). Reliability does NOT depend on this publish succeeding — the
            // reset state above + the dispatcher poll re-scrape the URL if the publish is lost.
            await messageBus.PublishAsync(new ScrapeProductUrlCommand(productUrl.Id, Force: true));

            logger.LogInformation("Retry scrape requested for URL {ProductUrlId} on product {ProductId}", productUrl.Id, product.Id);
        }
    }
}
