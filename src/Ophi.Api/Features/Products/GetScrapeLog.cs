using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Products;

public static class GetScrapeLog
{
    public static void MapGetScrapeLogEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/products/{id:guid}/scrape-log", async (Guid id, int? limit, IMessageBus bus, HttpContext context) =>
        {
            var query = new Query(id, context.User.GetUserId(), Math.Clamp(limit ?? 20, 1, 200));
            var result = await bus.InvokeAsync<Response>(query);
            return Results.Ok(result);
        })
        .WithName("GetScrapeLog")
        .WithTags("Products")
        .WithSummary("Get scrape history for a product")
        .WithDescription("Returns recent scrape attempts for a product URL with timing, success/failure status, extracted price, and error details. Limited to the most recent entries (default 20, max 200). Scrape logs are retained for 30 days.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Query(Guid ProductId, Guid UserId, int Limit);

    public record Response(List<ScrapeLogEntry> Items);

    public record ScrapeLogEntry(
        Guid Id,
        bool Success,
        decimal? Price,
        string? Error,
        int DurationMs,
        string? Url,
        bool IsOutOfStock,
        string StoreDomain,
        DateTime CreatedAt
    );

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching scrape log for product {ProductId} (limit={Limit})", request.ProductId, request.Limit);

            var productExists = await dbContext.Products
                .AsNoTracking()
                .AnyAsync(p => p.Id == request.ProductId && p.UserId == request.UserId, cancellationToken);

            if (!productExists)
                throw new NotFoundException("Product not found");

            var logs = await dbContext.ScrapeLogs
                .AsNoTracking()
                .Where(s => s.ProductId == request.ProductId)
                .OrderByDescending(s => s.CreatedAt)
                .Take(request.Limit)
                .Select(s => new ScrapeLogEntry(
                    s.Id,
                    s.Success,
                    s.Price,
                    s.Error,
                    s.DurationMs,
                    s.ProductUrl != null ? s.ProductUrl.Url : null,
                    s.IsOutOfStock,
                    s.StoreDomain,
                    s.CreatedAt
                ))
                .ToListAsync(cancellationToken);

            logger.LogDebug("Returning {Count} scrape log entries for product {ProductId}", logs.Count, request.ProductId);
            return new Response(logs);
        }
    }
}
