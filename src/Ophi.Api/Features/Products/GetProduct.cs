using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Common.Helpers;
using Ophi.Domain.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Products;

public static class GetProduct
{
    public static void MapGetProductEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/products/{id:guid}", async (Guid id, IMessageBus bus, HttpContext context) =>
        {
            var query = new Query(id, context.User.GetUserId());
            var result = await bus.InvokeAsync<Response>(query);
            return Results.Ok(result);
        })
        .WithName("GetProduct")
        .WithTags("Products")
        .WithSummary("Get a product by ID")
        .WithDescription("Returns full product details including all URLs, price statistics (min, max, average over 90 days), custom fields, tags, alerts, deal score, and currency mismatch detection.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Query(Guid ProductId, Guid UserId);

    public record Response(
         Guid Id,
        string Name,
        string Url,
        string? ImageUrl,
        decimal? CurrentPrice,
        decimal? PreviousPrice,
        decimal? PriceChange,
        string Currency,
        DateTime? LastChecked,
        string Status,
        bool IsFavourite,
        Guid? ComparisonGroupId,
        bool HasCurrencyMismatch,
        bool IsOutOfStock,
        StatisticsDto Statistics,
        List<AlertDto> Alerts,
        List<TagDto> Tags,
        List<ProductUrlDto> Urls,
        List<CustomFieldDto> CustomFields,
        int? DealScore = null,
        int? CheckIntervalMinutes = null,
        bool HasPriceAnomaly = false
    );

    public record ProductUrlDto(
        Guid Id,
        string Url,
        string? AffiliateUrl,
        string? StoreId,
        decimal? CurrentPrice,
        string Currency,
        DateTime? LastCheckedAt,
        string? LastError,
        int FailureCount,
        string Status,
        string? SuspiciousReason,
        bool IsOutOfStock
    );

    public record StatisticsDto( decimal Min, decimal Max, decimal Average, decimal? Current);

    /// <param name="Currency">
    /// Denomination of <paramref name="TargetPrice"/>, which is not necessarily the product's current
    /// currency — the client must format the target with this, not with the product's.
    /// </param>
    /// <param name="HasCurrencyMismatch">
    /// True when the target can no longer be compared against the product's price because the two are
    /// in different currencies. Such an alert is dormant and will not fire until they agree again.
    /// </param>
    public record AlertDto(
        Guid Id,
        decimal TargetPrice,
        string Condition,
        bool Active,
        DateTime? LastTriggered,
        string Currency,
        bool HasCurrencyMismatch);

    public record TagDto( Guid Id, string Name, string Color);

    public class Handler(OphiDbContext dbContext, TimeProvider timeProvider, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            var product = await dbContext.Products
                .AsNoTracking()
                .AsSplitQuery()
                .Include(p => p.ProductUrls)
                .Include(p => p.Alerts)
                .Include(p => p.ProductTags)
                .ThenInclude(pt => pt.Tag)
                .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.UserId == request.UserId, cancellationToken);

            if (product == null)
            {
                logger.LogWarning("Product {ProductId} not found for user {UserId}", request.ProductId, request.UserId);
                throw new NotFoundException("Product not found");
            }

            // Single query for 90 days — derive both statistics and 14-day sparkline from it
            var startDate = timeProvider.GetUtcNow().UtcDateTime.AddDays(-90);
            var allPricePoints = await dbContext.PricePoints
                // Statistics compare prices, so only the product's own currency (see GetProducts).
                .Where(pp => pp.ProductId == request.ProductId && pp.RecordedAt >= startDate &&
                             pp.Currency == product.Currency)
                .OrderBy(pp => pp.RecordedAt)
                .Select(pp => new { pp.Price, pp.RecordedAt })
                .ToListAsync(cancellationToken);

            var statistics = allPricePoints.Count > 0
                ? new StatisticsDto(
                    allPricePoints.Min(p => p.Price),
                    allPricePoints.Max(p => p.Price),
                    Math.Round(allPricePoints.Average(p => p.Price), 2),
                    product.CurrentPrice)
                : new StatisticsDto(0, 0, 0, product.CurrentPrice);

            // Map alerts
            var alerts = product.Alerts
                .Select(a => new AlertDto(
                    a.Id,
                    a.TargetPrice,
                    a.Condition.ToApiString(),
                    a.IsActive,
                    a.LastTriggeredAt,
                    a.Currency,
                    a.HasCurrencyMismatch(product.Currency)
                ))
                .ToList();

            // Map tags
            var tags = product.ProductTags
                .Select(pt => new TagDto(pt.Tag.Id, pt.Tag.Name, pt.Tag.Color))
                .ToList();

            var affiliates = await AffiliateUrlResolver.LoadAsync(dbContext, request.UserId, cancellationToken);

            // Map product URLs
            var urls = product.ProductUrls
                .Select(pu => new ProductUrlDto(
                    pu.Id, pu.Url, affiliates.Resolve(pu.Url, pu.StoreId), pu.StoreId, pu.CurrentPrice,
                    pu.Currency, pu.LastCheckedAt, pu.LastError, pu.FailureCount,
                    pu.Status.ToApiString(), pu.SuspiciousReason, pu.IsOutOfStock
                ))
                .ToList();

            // Detect currency mismatch across tracked URLs (only compare URLs that have been scraped)
            var hasCurrencyMismatch = product.ProductUrls
                .Where(pu => pu.LastCheckedAt != null)
                .Select(pu => pu.Currency)
                .Distinct()
                .Count() > 1;

            // Derive 14-day sparkline from the 90-day superset (no extra DB query)
            var sparklineCutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-14);
            var recentPrices = allPricePoints.Where(pp => pp.RecordedAt >= sparklineCutoff).ToList();

            List<GetProducts.SparklinePointDto>? sparklinePoints = null;
            decimal? sparklineMin = null, sparklineMax = null;
            if (recentPrices.Count > 0)
            {
                sparklinePoints = recentPrices
                    .GroupBy(pp => pp.RecordedAt.Date)
                    .OrderBy(g => g.Key)
                    .Select(g => new GetProducts.SparklinePointDto(g.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), g.Min(pp => pp.Price)))
                    .ToList();
                sparklineMin = recentPrices.Min(pp => pp.Price);
                sparklineMax = recentPrices.Max(pp => pp.Price);
            }

            var dealScore = GetProducts.ComputeDealScore(
                product.CurrentPrice, sparklineMin, sparklineMax, sparklinePoints, product.Alerts, product.Currency);

            // Calculate price change percentage
            decimal? priceChange = null;
            if (product is { CurrentPrice: not null, PreviousPrice: not null and not 0 })
            {
                priceChange = Math.Round(
                    (product.CurrentPrice.Value - product.PreviousPrice.Value) / product.PreviousPrice.Value * 100,
                    2
                );
            }

            var firstUrl = product.GetPrimaryUrl();
            var lastChecked = product.ProductUrls.Count != 0 ? product.ProductUrls.Max(pu => pu.LastCheckedAt) : null;

            // Product-level OOS: all URLs are out of stock
            var isOutOfStock = product.ProductUrls.Count > 0 && product.ProductUrls.All(pu => pu.IsOutOfStock);

            return new Response(
                product.Id,
                product.Name,
                firstUrl?.Url ?? "",
                product.ImageUrl,
                product.CurrentPrice,
                product.PreviousPrice,
                priceChange,
                product.Currency,
                lastChecked,
                product.Status.ToApiString(),
                product.IsFavourite,
                product.ComparisonGroupId,
                hasCurrencyMismatch,
                isOutOfStock,
                statistics,
                alerts,
                tags,
                urls,
                product.CustomFields.Select(cf => new CustomFieldDto(cf.Name, cf.Value)).ToList(),
                dealScore,
                product.CheckIntervalMinutes,
                product.HasPriceAnomaly
            );
        }
    }
}
