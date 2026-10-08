using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Common.Helpers;
using Ophi.Api.Features.Products;
using Ophi.Domain.Extensions;
using Ophi.Domain.Services;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Comparisons;

public static class GetComparisonGroup
{
    public record Query(Guid GroupId, Guid UserId, int Days = 30);

    public record Response(
        Guid Id,
        string Name,
        string? Description,
        List<ProductDto> Products,
        Guid? BestPriceProductId,
        decimal? BestPrice,
        DateTime UpdatedAt
    );

    public record ProductDto(
        Guid Id,
        string Name,
        string Url,
        string? AffiliateUrl,
        string? ImageUrl,
        decimal? CurrentPrice,
        string Currency,
        bool IsBestPrice,
        List<PriceHistoryPointDto> PriceHistory,
        List<CustomFieldDto> CustomFields
    );

    public record PriceHistoryPointDto( DateTime Date, decimal Price);

    public class Handler(OphiDbContext dbContext, TimeProvider timeProvider, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching comparison group {GroupId} for user {UserId}", request.GroupId, request.UserId);

            var group = await dbContext.ComparisonGroups
                .AsNoTracking()
                .FirstOrDefaultAsync(cg => cg.Id == request.GroupId && cg.UserId == request.UserId, cancellationToken) ??
                throw new NotFoundException("Comparison group not found");

            var days = Math.Clamp(request.Days, 1, 365);
            var startDate = timeProvider.GetUtcNow().UtcDateTime.AddDays(-days);

            // Get products in this group with their price history, sorted by price ascending (nulls last)
            var productEntities = await dbContext.Products
                .AsNoTracking()
                .AsSplitQuery()
                .Include(p => p.ProductUrls)
                .Include(p => p.PriceHistory.Where(ph => ph.RecordedAt >= startDate))
                .Where(p => p.ComparisonGroupId == request.GroupId)
                .OrderBy(p => p.CurrentPrice == null)
                .ThenBy(p => (double?)p.CurrentPrice)
                .ToListAsync(cancellationToken);

            var affiliates = await AffiliateUrlResolver.LoadAsync(dbContext, request.UserId, cancellationToken);

            var products = productEntities.Select(p =>
            {
                var firstUrl = p.GetPrimaryUrl();
                var url = firstUrl?.Url ?? "";
                var affiliateUrl = affiliates.Resolve(url, firstUrl?.StoreId);

                return new
                {
                    p.Id,
                    p.Name,
                    Url = url,
                    AffiliateUrl = affiliateUrl,
                    p.ImageUrl,
                    p.CurrentPrice,
                    p.Currency,
                // History points carry their own currency; a URL re-pointed to another region
                // leaves points the product's currency can't be plotted against.
                PriceHistory = p.PriceHistory
                    .Where(ph => string.Equals(ph.Currency, p.Currency, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(ph => ph.RecordedAt)
                    .Select(ph => new PriceHistoryPointDto(ph.RecordedAt.Date, ph.Price))
                    .ToList(),
                CustomFields = p.CustomFields
                    .Select(cf => new CustomFieldDto(cf.Name, cf.Value))
                    .ToList()
                };
            }).ToList();

            // Best price: choose the currency first, then the MIN inside it. EUR 80 is not
            // cheaper than USD 100, and rates are display-only (docs/agent-notes.md § Pricing).
            var productsWithPrice = products.Where(p => p.CurrentPrice.HasValue).ToList();
            Guid? bestPriceProductId = null;
            decimal? bestPrice = null;

            var bestCurrency = ProductPriceAggregator.DominantCurrency(productsWithPrice.Select(p => p.Currency));
            if (bestCurrency != null)
            {
                var bestProduct = productsWithPrice
                    .Where(p => string.Equals(p.Currency, bestCurrency, StringComparison.OrdinalIgnoreCase))
                    .MinBy(p => p.CurrentPrice!.Value)!;
                bestPriceProductId = bestProduct.Id;
                bestPrice = bestProduct.CurrentPrice;
            }

            // Map to DTOs with IsBestPrice flag
            var productDtos = products.Select(p => new ProductDto(
                p.Id,
                p.Name,
                p.Url,
                p.AffiliateUrl,
                p.ImageUrl,
                p.CurrentPrice,
                p.Currency,
                p.Id == bestPriceProductId,
                p.PriceHistory,
                p.CustomFields
            )).ToList();

            logger.LogDebug("Returning comparison group {GroupId} with {ProductCount} products", group.Id, productDtos.Count);

            return new Response(
                group.Id,
                group.Name,
                group.Description,
                productDtos,
                bestPriceProductId,
                bestPrice,
                group.UpdatedAt
            );
        }
    }

    public static void MapGetComparisonGroupEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/v1/comparisons/{id:guid}", async (Guid id, int? days, IMessageBus bus, HttpContext context) =>
        {
            var query = new Query(id, context.User.GetUserId(), days ?? 30);
            var result = await bus.InvokeAsync<Response>(query);
            return Results.Ok(result);
        })
        .WithName("GetComparisonGroup")
        .WithTags("Comparisons")
        .WithSummary("Get a comparison group")
        .WithDescription("Returns a comparison group with all member products, their current prices, price history, and the overall best price. Products are sorted by current price ascending.")
        .Produces<Response>(200)
        .RequireAuthorization();
}
