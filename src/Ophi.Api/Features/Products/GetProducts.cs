using System.Globalization;
using System.Linq.Expressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Common.Helpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Domain.Extensions;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Products;

public static class GetProducts
{
    public static void MapGetProductsEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/products", async (Guid? tagId, string? search, string? status, string? sortBy, string? sortDirection, int? page, int? pageSize, bool? includeSparkline, bool? atLowest, bool? favourite, bool? priceDrop, bool? hasAlerts, IMessageBus bus, HttpContext context) =>
        {
            var query = new Query(context.User.GetUserId(), tagId, search, status, sortBy, sortDirection, page, pageSize, includeSparkline, atLowest, favourite, priceDrop, hasAlerts);
            var result = await bus.InvokeAsync<Response>(query);
            return Results.Ok(result);
        })
        .WithName("GetProducts")
        .WithTags("Products")
        .WithSummary("List products with filtering")
        .WithDescription("Returns a paginated list of products for the current user. Supports filtering by tag, search text, and status. Sortable by name, price, or lastChecked. Pass includeSparkline=true to include 14-day price history, min/max prices, and deal scores (more expensive query).")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Query(
        Guid UserId,
        Guid? TagId = null,
        string? Search = null,
        string? Status = null,
        string? SortBy = null,
        string? SortDirection = null,
        int? Page = null,
        int? PageSize = null,
        bool? IncludeSparkline = null,
        bool? AtLowest = null,
        bool? Favourite = null,
        bool? PriceDrop = null,
        bool? HasAlerts = null
    );

    public record Response(
        List<ProductDto> Items,
        int Total,
        int Page,
        int PageSize,
        int AtLowestCount = 0,
        int PriceDropCount = 0,
        int WithAlertsCount = 0);

    public record ProductDto(
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
        bool IsOutOfStock,
        int StoreCount,
        int AlertCount,
        List<TagDto> Tags,
        List<CustomFieldDto> CustomFields,
        List<SparklinePointDto>? Sparkline = null,
        decimal? PriceMin = null,
        decimal? PriceMax = null,
        int? DealScore = null,
        string? AffiliateUrl = null,
        int? CheckIntervalMinutes = null,
        bool HasPriceAnomaly = false
    );

    public record SparklinePointDto(string Date, decimal Price);

    public record TagDto( Guid Id, string Name, string Color);

    /// <summary>
    /// Computes a 0-100 deal score based on price position, trend, and alert proximity.
    /// </summary>
    internal static int? ComputeDealScore(
        decimal? currentPrice,
        decimal? priceMin,
        decimal? priceMax,
        List<SparklinePointDto>? sparkline,
        ICollection<Domain.Entities.Alert> alerts,
        string productCurrency)
    {
        if (currentPrice == null || priceMin == null || priceMax == null || sparkline == null || sparkline.Count < 2)
            return null;

        // 1. Distance from low (40% weight): 100 = at min, 0 = at max
        double distanceScore;
        if (priceMax == priceMin)
        {
            distanceScore = 50;
        }
        else
        {
            distanceScore = (double)(1m - (currentPrice.Value - priceMin.Value) / (priceMax.Value - priceMin.Value)) * 100;
        }

        // 2. Trend direction (30% weight): compare avg of last 3 to avg of first 3
        var firstN = sparkline.Take(Math.Min(3, sparkline.Count)).Average(s => (double)s.Price);
        var lastN = sparkline.Skip(Math.Max(0, sparkline.Count - 3)).Average(s => (double)s.Price);
        double trendScore;
        if (firstN == 0)
        {
            trendScore = 50;
        }
        else
        {
            var trendPct = (lastN - firstN) / firstN;
            // -20% decline -> score 100, +20% rise -> score 0, linear between
            trendScore = Math.Clamp(50 - trendPct * 250, 0, 100);
        }

        // 3. Alert proximity (30% weight)
        double alertScore;
        var belowAlertTargets = alerts
            // A target in another currency (dormant since a re-point) is not comparable to the price.
            .Where(a => a.IsActive && a.Condition == Domain.Enums.AlertCondition.Below &&
                        !a.HasCurrencyMismatch(productCurrency))
            .Select(a => a.TargetPrice)
            .ToList();

        if (belowAlertTargets.Count == 0)
        {
            alertScore = 50; // Neutral when no alerts
        }
        else
        {
            // Find the closest "below" alert target
            var bestProximity = belowAlertTargets
                .Select(tp => (double)(currentPrice.Value - tp) / (double)currentPrice.Value)
                .Min();

            if (bestProximity <= 0)
            {
                alertScore = 100; // Already below target
            }
            else
            {
                // 0% gap = 100, 50%+ gap = 0
                alertScore = Math.Clamp((1 - bestProximity * 2) * 100, 0, 100);
            }
        }

        var score = distanceScore * 0.40 + trendScore * 0.30 + alertScore * 0.30;
        return (int)Math.Round(Math.Clamp(score, 0, 100));
    }

    public class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(x => x.Search)
                .MaximumLength(100).WithMessage("Search term must not exceed 100 characters")
                .When(x => x.Search != null);
        }
    }

    public class Handler(OphiDbContext dbContext, TimeProvider timeProvider, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching products for user {UserId} (page={Page}, search={Search}, status={Status})",
                request.UserId, request.Page, request.Search, request.Status);

            var page = Math.Max(request.Page ?? 1, 1);
            var pageSize = Math.Clamp(request.PageSize ?? PaginationDefaults.DefaultPageSize, 1, PaginationDefaults.MaxPageSize);

            var query = dbContext.Products
                .AsNoTracking()
                .AsSplitQuery()
                .Include(p => p.ProductUrls)
                .Include(p => p.ProductTags)
                .ThenInclude(pt => pt.Tag)
                .Where(p => p.UserId == request.UserId);

            if (request.TagId.HasValue)
            {
                query = query.Where(p => p.ProductTags.Any(pt => pt.TagId == request.TagId));
            }

            // Search filter — lower both sides + LIKE so it's case-insensitive on BOTH providers.
            // (Postgres LIKE is case-sensitive; SQLite's is not. EF.Functions.ILike is Npgsql-only and
            // would break the SQLite unit tier, so we use the portable lower()+LIKE form instead.)
            // LikePattern escapes %/_ in the user term so literal wildcards match literally, and the
            // 3-arg Like emits an ESCAPE clause (supported on both providers).
            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var search = LikePattern.Contains(request.Search.Trim());
                query = query.Where(p =>
                    EF.Functions.Like(p.Name.ToLower(), search, LikePattern.EscapeChar) ||
                    p.ProductUrls.Any(pu => EF.Functions.Like(pu.Url.ToLower(), search, LikePattern.EscapeChar)));
            }

            // Status filter
            if (!string.IsNullOrWhiteSpace(request.Status) &&
                Enum.TryParse<ProductStatus>(request.Status, ignoreCase: true, out var parsedStatus))
            {
                query = query.Where(p => p.Status == parsedStatus);
            }

            // Only history in the product's own currency is comparable: a URL re-pointed to another
            // region leaves points in a currency the current price can't be measured against.
            Expression<Func<Product, bool>> isAtLowest = p =>
                p.CurrentPrice != null &&
                p.PriceHistory.Any(pp => pp.Currency == p.Currency) &&
                p.CurrentPrice == p.PriceHistory.Where(pp => pp.Currency == p.Currency).Min(pp => pp.Price);

            if (request.AtLowest == true)
            {
                query = query.Where(isAtLowest);
            }

            // Server-side stats-bar filters: keep these in the DB so they compose with pagination
            // (a client-side filter over one page shows misleading subsets — see frontend-ux-plan UX-2).
            if (request.Favourite == true)
            {
                query = query.Where(p => p.IsFavourite);
            }

            // "Price drop" mirrors the DTO's PriceChange < 0: both prices known, previous != 0, current lower.
            if (request.PriceDrop == true)
            {
                query = query.Where(p =>
                    p.CurrentPrice != null && p.PreviousPrice != null && p.PreviousPrice != 0 &&
                    p.CurrentPrice < p.PreviousPrice);
            }

            if (request.HasAlerts == true)
            {
                query = query.Where(p => p.Alerts.Any());
            }

            // Sort - favourites always first, then by specified sort
            var sortDirection = string.Equals(request.SortDirection, "desc", StringComparison.OrdinalIgnoreCase)
                ? "desc" : "asc";

            // First order by IsFavourite descending (true = 1, false = 0, so descending puts favourites first)
            var favouritesFirst = query.OrderByDescending(p => p.IsFavourite);

            var orderedQuery = request.SortBy?.ToLowerInvariant() switch
            {
                "name" => sortDirection == "desc"
                    ? favouritesFirst.ThenByDescending(p => p.Name)
                    : favouritesFirst.ThenBy(p => p.Name),
                "price" => sortDirection == "desc"
                    ? favouritesFirst.ThenByDescending(p => (double?)p.CurrentPrice)
                    : favouritesFirst.ThenBy(p => (double?)p.CurrentPrice),
                "dateadded" => sortDirection == "desc"
                    ? favouritesFirst.ThenByDescending(p => p.CreatedAt)
                    : favouritesFirst.ThenBy(p => p.CreatedAt),
                "lastchecked" => sortDirection == "desc"
                    ? favouritesFirst.ThenByDescending(p => p.ProductUrls.Max(pu => pu.LastCheckedAt))
                    : favouritesFirst.ThenBy(p => p.ProductUrls.Max(pu => pu.LastCheckedAt)),
                "pricechange" => sortDirection == "desc"
                    ? favouritesFirst.ThenByDescending(p =>
                        p.CurrentPrice.HasValue && p.PreviousPrice.HasValue && p.PreviousPrice != 0
                            ? (double?)((p.CurrentPrice.Value - p.PreviousPrice.Value) / p.PreviousPrice.Value * 100)
                            : null)
                    : favouritesFirst.ThenBy(p =>
                        p.CurrentPrice.HasValue && p.PreviousPrice.HasValue && p.PreviousPrice != 0
                            ? (double?)((p.CurrentPrice.Value - p.PreviousPrice.Value) / p.PreviousPrice.Value * 100)
                            : null),
                _ => favouritesFirst.ThenByDescending(p => p.UpdatedAt)
            };

            // Unique final key. Every sort option above ends on a non-unique column (prices tie
            // constantly), and SQL guarantees nothing about the order of tied rows without one — so
            // Skip/Take may return the same product on two pages and drop another entirely.
            // Correctness by contract rather than by accident of the query plan: today both providers
            // happen to be stable at small row counts, which is exactly why the defect would surface
            // only in production, at volume, once the planner picks a different scan.
            orderedQuery = orderedQuery.ThenBy(p => p.Id);

            // Stats-bar counts are computed against the global user scope (unfiltered) in one DB
            // roundtrip, so each number stays independent of whatever filter is currently applied —
            // the bar always describes the whole watchlist, the list shows the filtered slice.
            var counts = await dbContext.Products
                .AsNoTracking()
                .Where(p => p.UserId == request.UserId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    AtLowest = g.Count(p =>
                        p.CurrentPrice != null &&
                        p.PriceHistory.Any(pp => pp.Currency == p.Currency) &&
                        p.CurrentPrice == p.PriceHistory.Where(pp => pp.Currency == p.Currency).Min(pp => pp.Price)),
                    PriceDrops = g.Count(p =>
                        p.CurrentPrice != null && p.PreviousPrice != null && p.PreviousPrice != 0 &&
                        p.CurrentPrice < p.PreviousPrice),
                    WithAlerts = g.Count(p => p.Alerts.Any())
                })
                .FirstOrDefaultAsync(cancellationToken);

            var atLowestCount = counts?.AtLowest ?? 0;
            var priceDropCount = counts?.PriceDrops ?? 0;
            var withAlertsCount = counts?.WithAlerts ?? 0;
            var total = await query.CountAsync(cancellationToken);

            var entities = await orderedQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            // Load per-product alert summary in one round-trip instead of Include(Alerts).
            // Need: total count + Below-condition target prices (for ComputeDealScore).
            var entityIds = entities.Select(e => e.Id).ToList();
            var alertSummaries = entityIds.Count == 0
                ? new Dictionary<Guid, (int Count, List<Alert> BelowAlerts)>()
                : await dbContext.Alerts
                    .AsNoTracking()
                    .Where(a => entityIds.Contains(a.ProductId))
                    .GroupBy(a => a.ProductId)
                    .Select(g => new
                    {
                        ProductId = g.Key,
                        Count = g.Count(),
                        BelowAlerts = g
                            .Where(a => a.IsActive && a.Condition == AlertCondition.Below)
                            .Select(a => new Alert
                            {
                                TargetPrice = a.TargetPrice,
                                Condition = a.Condition,
                                IsActive = a.IsActive,
                                Currency = a.Currency
                            })
                            .ToList()
                    })
                    .ToDictionaryAsync(x => x.ProductId, x => (x.Count, x.BelowAlerts), cancellationToken);

            // Batch-load sparkline data when requested
            Dictionary<Guid, List<SparklinePointDto>>? sparklineData = null;
            Dictionary<Guid, (decimal Min, decimal Max)>? priceStats = null;

            if (request.IncludeSparkline == true && entities.Count > 0)
            {
                var productIds = entities.Select(e => e.Id).ToList();
                var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-14);

                var pricePoints = await dbContext.PricePoints
                    .Where(pp => productIds.Contains(pp.ProductId) && pp.RecordedAt >= cutoff &&
                                 pp.Currency == pp.Product.Currency)
                    .OrderBy(pp => pp.RecordedAt)
                    .Select(pp => new { pp.ProductId, pp.Price, pp.RecordedAt })
                    .ToListAsync(cancellationToken);

                var grouped = pricePoints.GroupBy(pp => pp.ProductId).ToList();

                sparklineData = grouped.ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(pp => pp.RecordedAt.Date)
                          .OrderBy(dg => dg.Key)
                          // The day's low, matching the product price (the MIN across URLs).
                          .Select(dg => new SparklinePointDto(
                              dg.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                              dg.Min(pp => pp.Price)))
                          .ToList()
                );

                priceStats = grouped.ToDictionary(
                    g => g.Key,
                    g => (Min: g.Min(pp => pp.Price), Max: g.Max(pp => pp.Price))
                );
            }

            var affiliates = await AffiliateUrlResolver.LoadAsync(dbContext, request.UserId, cancellationToken);

            var products = entities.Select(p =>
            {
                List<SparklinePointDto>? sp = null;
                sparklineData?.TryGetValue(p.Id, out sp);
                (decimal Min, decimal Max)? ps = null;
                if (priceStats != null && priceStats.TryGetValue(p.Id, out var psVal))
                    ps = psVal;

                var firstUrl = p.GetPrimaryUrl();
                var url = firstUrl?.Url ?? "";
                var affiliateUrl = affiliates.Resolve(url, firstUrl?.StoreId);

                var alertSummary = alertSummaries.TryGetValue(p.Id, out var asum)
                    ? asum
                    : (Count: 0, BelowAlerts: new List<Alert>());

                return new ProductDto(
                    p.Id,
                    p.Name,
                    url,
                    p.ImageUrl,
                    p.CurrentPrice,
                    p.PreviousPrice,
                    p is { CurrentPrice: not null, PreviousPrice: not null and not 0 }
                        ? Math.Round((p.CurrentPrice.Value - p.PreviousPrice.Value) / p.PreviousPrice.Value * 100, 2)
                        : null,
                    p.Currency,
                    p.ProductUrls.Count != 0 ? p.ProductUrls.Max(pu => pu.LastCheckedAt) : null,
                    p.Status.ToApiString(),
                    p.IsFavourite,
                    p.ProductUrls.Count > 0 && p.ProductUrls.All(pu => pu.IsOutOfStock),
                    p.ProductUrls.Count,
                    alertSummary.Count,
                    p.ProductTags.Select(pt => new TagDto(pt.Tag.Id, pt.Tag.Name, pt.Tag.Color)).ToList(),
                    p.CustomFields.Select(cf => new CustomFieldDto(cf.Name, cf.Value)).ToList(),
                    sparklineData != null ? (sp ?? new List<SparklinePointDto>()) : null,
                    ps?.Min,
                    ps?.Max,
                    sparklineData != null
                        ? ComputeDealScore(p.CurrentPrice, ps?.Min, ps?.Max, sp, alertSummary.BelowAlerts, p.Currency)
                        : null,
                    affiliateUrl,
                    p.CheckIntervalMinutes,
                    p.HasPriceAnomaly
                );
            }).ToList();

            logger.LogDebug("Returning {Count}/{Total} products for user {UserId}", products.Count, total, request.UserId);
            return new Response(products, total, page, pageSize, atLowestCount, priceDropCount, withAlertsCount);
        }
    }
}
