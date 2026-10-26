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
using Ophi.Domain.Services;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Products;

public static class GetProducts
{
    public static void MapGetProductsEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/products", async (Guid? tagId, string? search, string? status, string? sortBy, string? sortDirection, int? page, int? pageSize, bool? includeSparkline, bool? atLowest, bool? favourite, bool? priceDrop, bool? hasAlerts, bool? needsAction, bool? nearTarget, string? storeId, IMessageBus bus, HttpContext context) =>
        {
            var query = new Query(context.User.GetUserId(), tagId, search, status, sortBy, sortDirection, page, pageSize, includeSparkline, atLowest, favourite, priceDrop, hasAlerts, needsAction, nearTarget, storeId);
            var result = await bus.InvokeAsync<Response>(query);
            return Results.Ok(result);
        })
        .WithName("GetProducts")
        .WithTags("Products")
        .WithSummary("List products with filtering")
        .WithDescription("Returns a paginated list of products for the current user. Supports filtering by tag, search text, and status. Sortable by name, price (grouped by currency, then by price), pricechange (change7d), dateadded, lastchecked, or favourites (favourites first, then newest); no sort pins favourites otherwise. Each item carries `change7d`, the percent change of the product price over 7 days across the same URLs (null with less than 7 days of history), and `trackedSince`, its first price point; priceDrop=true and priceDropCount use change7d < 0. Pass includeSparkline=true to include the product price at the end of each of the last 30 days and the 90-day price range, which includes the current price (more expensive query). atLowest means at a real 90-day low: a first price point 30+ days old and a different price in the window. Each item carries `attention` (its worst URL state, or null) and `closestAlert` (the active below/above target nearest the price, in the product's currency); needsAction=true and nearTarget=true filter on them, and needsActionCount/nearTargetCount count them account-wide. productCount, urlCount and stores (the stores the account's URLs use) are account-wide too.")
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
        bool? HasAlerts = null,
        bool? NeedsAction = null,
        bool? NearTarget = null,
        string? StoreId = null
    );

    public record Response(
        List<ProductDto> Items,
        int Total,
        int Page,
        int PageSize,
        int AtLowestCount = 0,
        int PriceDropCount = 0,
        int WithAlertsCount = 0,
        int NeedsActionCount = 0,
        int NearTargetCount = 0,
        int ProductCount = 0,
        int UrlCount = 0,
        List<StoreOptionDto>? Stores = null);

    /// <summary>A store the account's URLs use; <c>Name</c> falls back to the id without a configuration.</summary>
    public record StoreOptionDto(string StoreId, string Name);

    /// <summary>The daily-low sparkline covers this many days.</summary>
    public const int SparklineDays = 30;

    /// <summary>PriceMin/PriceMax and the "at lowest" filter cover this many days.</summary>
    public const int RangeDays = 90;

    /// <summary><c>Change7d</c>, the price-drop filter and the price-change sort compare with this many days ago.</summary>
    public const int ChangeDays = 7;

    /// <summary>A product is at a real low only once its first price point is this many days old.</summary>
    public const int RealLowMinDays = 30;

    /// <summary>
    /// At a real low: at or below every price held in the last <see cref="RangeDays"/> days — the
    /// points inside the window and the price each URL held when it opened — with at least one of
    /// them different, and a first price point at least <see cref="RealLowMinDays"/> days old. In
    /// the product's own currency: a URL re-pointed to another region leaves points the price can't
    /// be measured against. The filter and the count share it, so the chip's number equals its rows.
    /// </summary>
    internal static Expression<Func<Product, bool>> AtRealLow(DateTime now)
    {
        var windowStart = now.AddDays(-RangeDays);
        var matured = now.AddDays(-RealLowMinDays);
        Expression<Func<Product, IEnumerable<PricePoint>>> windowPrices = p => p.PriceHistory.Where(pp =>
            pp.Currency == p.Currency &&
            (pp.RecordedAt >= windowStart ||
             !p.PriceHistory.Any(q => q.ProductUrlId == pp.ProductUrlId && q.RecordedAt > pp.RecordedAt && q.RecordedAt < windowStart)));
        Expression<Func<Product, IEnumerable<PricePoint>, bool>> rule = (p, window) =>
            p.CurrentPrice != null &&
            p.PriceHistory.Any(pp => pp.RecordedAt <= matured) &&
            window.Any(pp => pp.Price != p.CurrentPrice) &&
            p.CurrentPrice <= window.Min(pp => pp.Price);

        var product = Expression.Parameter(typeof(Product), "p");
        return Expression.Lambda<Func<Product, bool>>(
            Inline(rule, product, Inline(windowPrices, product)), product);
    }

    /// <summary>
    /// The change of the product price since <paramref name="cutoff"/>, in percent, over the same
    /// URLs at both ends: those that held a price in the product's currency at the cutoff and are
    /// live now (not paused, in stock, priced). Adding or removing a URL is therefore not a change.
    /// Null when no URL is in both sets — including a product with less history than the window.
    /// One expression serves the value, the filter, the count and the sort.
    /// </summary>
    internal static Expression<Func<Product, decimal?>> Change7d(DateTime cutoff)
    {
        Expression<Func<Product, IEnumerable<ProductUrl>>> sameUrls = p => p.ProductUrls.Where(u =>
            u.Status != ProductUrlStatus.Paused && !u.IsOutOfStock && u.CurrentPrice != null && u.Currency == p.Currency &&
            u.PriceHistory.Where(pp => pp.RecordedAt <= cutoff).OrderByDescending(pp => pp.RecordedAt)
                .Select(pp => pp.Currency).FirstOrDefault() == p.Currency);
        Expression<Func<IEnumerable<ProductUrl>, decimal?>> thenMin = urls => urls.Min(u =>
            u.PriceHistory.Where(pp => pp.RecordedAt <= cutoff).OrderByDescending(pp => pp.RecordedAt)
                .Select(pp => (decimal?)pp.Price).FirstOrDefault());
        Expression<Func<IEnumerable<ProductUrl>, decimal?>> nowMin = urls => urls.Min(u => u.CurrentPrice);
        Expression<Func<decimal?, decimal?, decimal?>> percent = (current, then) =>
            then > 0 ? (current - then) / then * 100 : null;

        var product = Expression.Parameter(typeof(Product), "p");
        var urls = Inline(sameUrls, product);
        return Expression.Lambda<Func<Product, decimal?>>(
            Inline(percent, Inline(nowMin, urls), Inline(thenMin, urls)), product);
    }

    /// <summary>Builds <c>p => body(p, value(p))</c>, so a filter, a sort or a projection can use <paramref name="value"/>.</summary>
    private static Expression<Func<Product, TOut>> With<TOut>(
        Expression<Func<Product, decimal?>> value, Expression<Func<Product, decimal?, TOut>> body)
    {
        var product = body.Parameters[0];
        return Expression.Lambda<Func<Product, TOut>>(Inline(body, product, Inline(value, product)), product);
    }

    /// <summary>The body of <paramref name="lambda"/> with its parameters replaced by <paramref name="args"/>.</summary>
    private static Expression Inline(LambdaExpression lambda, params Expression[] args) =>
        new Substitution(lambda.Parameters.Zip(args).ToDictionary(pair => (Expression)pair.First, pair => pair.Second))
            .Visit(lambda.Body)!;

    private sealed class Substitution(Dictionary<Expression, Expression> map) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node) =>
            node is not null && map.TryGetValue(node, out var replacement) ? replacement : base.Visit(node);
    }

    private sealed record RowValues(Guid Id, decimal? Change7d, DateTime? TrackedSince);

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
        string? AffiliateUrl = null,
        int? CheckIntervalMinutes = null,
        bool HasPriceAnomaly = false,
        AttentionDto? Attention = null,
        AlertTargetDto? ClosestAlert = null,
        string? BestUrl = null,
        string? BestStoreName = null,
        DateTime? LastSucceededAt = null,
        decimal? Change7d = null,
        DateTime? TrackedSince = null
    );

    /// <summary>
    /// The product's worst URL state (<see cref="ProductAttention.Worst"/>) and the URL to act on.
    /// <c>Kind</c> is the camelCase <see cref="AttentionKind"/>; <c>IsAction</c> is false for the
    /// information-only states (out of stock, paused).
    /// </summary>
    public record AttentionDto(
        string Kind,
        bool IsAction,
        Guid ProductUrlId,
        string Url,
        int ConsecutiveFailures,
        DateTime? BackoffUntil,
        string? LastError);

    /// <summary>
    /// The alert target nearest the current price (<see cref="AlertProximity.Closest"/>). <c>Gap</c>
    /// is how far the price still has to move, in the product's currency; zero or less means reached.
    /// </summary>
    public record AlertTargetDto(Guid AlertId, string Condition, decimal TargetPrice, decimal Gap, bool IsNear);

    public record SparklinePointDto(string Date, decimal Price);

    public record TagDto( Guid Id, string Name, string Color);

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

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var isAtLowest = AtRealLow(now);
            var change7d = Change7d(now.AddDays(-ChangeDays));
            var isPriceDrop = With(change7d, (p, change) => change < 0m);

            if (!string.IsNullOrWhiteSpace(request.StoreId))
            {
                query = query.Where(p => p.ProductUrls.Any(pu => pu.StoreId == request.StoreId));
            }

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

            // "Price drop" is the DTO's Change7d < 0.
            if (request.PriceDrop == true)
            {
                query = query.Where(isPriceDrop);
            }

            if (request.HasAlerts == true)
            {
                query = query.Where(p => p.Alerts.Any());
            }

            // The same predicates feed the counts below, so a chip's number always equals its rows.
            if (request.NeedsAction == true)
            {
                query = query.Where(ProductAttention.NeedsAction);
            }

            if (request.NearTarget == true)
            {
                query = query.Where(AlertProximity.NearTarget);
            }

            // Favourites are first only when asked for: a silent pin made every other sort lie about
            // its order.
            var sortDirection = string.Equals(request.SortDirection, "desc", StringComparison.OrdinalIgnoreCase)
                ? "desc" : "asc";

            var orderedQuery = request.SortBy?.ToLowerInvariant() switch
            {
                "name" => sortDirection == "desc"
                    ? query.OrderByDescending(p => p.Name)
                    : query.OrderBy(p => p.Name),
                // Prices in different currencies are not comparable (no conversion, agent-notes
                // § Pricing), so the currency groups them first.
                "price" => sortDirection == "desc"
                    ? query.OrderBy(p => p.Currency).ThenByDescending(p => (double?)p.CurrentPrice)
                    : query.OrderBy(p => p.Currency).ThenBy(p => (double?)p.CurrentPrice),
                "dateadded" => sortDirection == "desc"
                    ? query.OrderByDescending(p => p.CreatedAt)
                    : query.OrderBy(p => p.CreatedAt),
                "lastchecked" => sortDirection == "desc"
                    ? query.OrderByDescending(p => p.ProductUrls.Max(pu => pu.LastCheckedAt))
                    : query.OrderBy(p => p.ProductUrls.Max(pu => pu.LastCheckedAt)),
                "pricechange" => sortDirection == "desc"
                    ? query.OrderByDescending(With(change7d, (p, change) => (double?)change))
                    : query.OrderBy(With(change7d, (p, change) => (double?)change)),
                "favourites" => query.OrderByDescending(p => p.IsFavourite).ThenByDescending(p => p.UpdatedAt),
                _ => query.OrderByDescending(p => p.UpdatedAt)
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
            var userProducts = dbContext.Products
                .AsNoTracking()
                .Where(p => p.UserId == request.UserId);
            var counts = await userProducts
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Products = g.Count(),
                    Urls = g.Sum(p => p.ProductUrls.Count),
                    WithAlerts = g.Count(p => p.Alerts.Any())
                })
                .FirstOrDefaultAsync(cancellationToken);
            // Separate counts: the shared predicates are Expressions, which a g.Count(...) lambda
            // cannot embed without restating them.
            var needsActionCount = await userProducts.CountAsync(ProductAttention.NeedsAction, cancellationToken);
            var atLowestCount = await userProducts.CountAsync(isAtLowest, cancellationToken);
            var nearTargetCount = await userProducts.CountAsync(AlertProximity.NearTarget, cancellationToken);
            var priceDropCount = await userProducts.CountAsync(isPriceDrop, cancellationToken);

            var productCount = counts?.Products ?? 0;
            var urlCount = counts?.Urls ?? 0;
            var withAlertsCount = counts?.WithAlerts ?? 0;
            var total = await query.CountAsync(cancellationToken);

            // Store names for the filter and each row's best store. Configurations are per user and
            // few, so one small read serves both.
            var storeNames = await dbContext.StoreConfigurations
                .AsNoTracking()
                .Where(sc => sc.UserId == request.UserId)
                .Select(sc => new { sc.StoreId, sc.Name })
                .ToDictionaryAsync(sc => sc.StoreId, sc => sc.Name, cancellationToken);
            var storeIdsInUse = await dbContext.ProductUrls
                .AsNoTracking()
                .Where(pu => pu.Product.UserId == request.UserId && pu.StoreId != null)
                .Select(pu => pu.StoreId!)
                .Distinct()
                .ToListAsync(cancellationToken);
            var stores = storeIdsInUse
                .Select(id => new StoreOptionDto(id, storeNames.GetValueOrDefault(id) ?? id))
                .OrderBy(st => st.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(st => st.StoreId, StringComparer.Ordinal)
                .ToList();

            var entities = await orderedQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            // Load per-product alert summary in one round-trip instead of Include(Alerts).
            // Need: total count + the active below/above targets (AlertProximity).
            var entityIds = entities.Select(e => e.Id).ToList();
            var alertSummaries = entityIds.Count == 0
                ? new Dictionary<Guid, (int Count, List<Alert> TargetAlerts)>()
                : await dbContext.Alerts
                    .AsNoTracking()
                    .Where(a => entityIds.Contains(a.ProductId))
                    .GroupBy(a => a.ProductId)
                    .Select(g => new
                    {
                        ProductId = g.Key,
                        Count = g.Count(),
                        TargetAlerts = g
                            .Where(a => a.IsActive && a.Condition != AlertCondition.PercentDrop)
                            .Select(a => new Alert
                            {
                                Id = a.Id,
                                TargetPrice = a.TargetPrice,
                                Condition = a.Condition,
                                IsActive = a.IsActive,
                                Currency = a.Currency
                            })
                            .ToList()
                    })
                    .ToDictionaryAsync(x => x.ProductId, x => (x.Count, x.TargetAlerts), cancellationToken);

            // Batch-load sparkline data when requested
            Dictionary<Guid, List<SparklinePointDto>>? sparklineData = null;
            Dictionary<Guid, (decimal Min, decimal Max)>? priceStats = null;

            // The 7-day change and the first price point, for this page only.
            var rowValues = entityIds.Count == 0
                ? new Dictionary<Guid, RowValues>()
                : await dbContext.Products
                    .AsNoTracking()
                    .Where(p => entityIds.Contains(p.Id))
                    .Select(With(change7d, (p, change) =>
                        new RowValues(p.Id, change, p.PriceHistory.Min(pp => (DateTime?)pp.RecordedAt))))
                    .ToDictionaryAsync(r => r.Id, cancellationToken);

            if (request.IncludeSparkline == true && entities.Count > 0)
            {
                var productIds = entities.Select(e => e.Id).ToList();
                var rangeStart = now.AddDays(-RangeDays);

                // Points are written only when a price changes. So the series needs the points in the
                // window and the price each URL held when it opened: its latest point before.
                var points = await dbContext.PricePoints
                    .AsNoTracking()
                    .Where(pp => productIds.Contains(pp.ProductId) &&
                                 (pp.RecordedAt >= rangeStart ||
                                  !dbContext.PricePoints.Any(q => q.ProductId == pp.ProductId &&
                                      q.ProductUrlId == pp.ProductUrlId &&
                                      q.RecordedAt > pp.RecordedAt && q.RecordedAt < rangeStart)))
                    .Select(pp => new { pp.ProductId, pp.ProductUrlId, pp.RecordedAt, pp.Price, pp.Currency })
                    .ToListAsync(cancellationToken);
                var pointsByProduct = points.ToLookup(pp => pp.ProductId);

                sparklineData = [];
                priceStats = [];
                foreach (var entity in entities)
                {
                    // A URL that is gone (null id) or no longer scraped does not hold its last price up to now.
                    var urls = pointsByProduct[entity.Id]
                        .GroupBy(pp => pp.ProductUrlId)
                        .Select(g => new PriceSeries.UrlSeries(
                            g.OrderBy(pp => pp.RecordedAt)
                                .Select(pp => new PriceSeries.Point(pp.RecordedAt, pp.Price, pp.Currency))
                                .ToList(),
                            entity.ProductUrls.FirstOrDefault(u => u.Id == g.Key) is { } url &&
                            url.Status != ProductUrlStatus.Paused && !url.IsOutOfStock))
                        .ToList();

                    sparklineData[entity.Id] = PriceSeries.Daily(urls, entity.Currency, now, SparklineDays)
                        .Select(d => new SparklinePointDto(d.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), d.Price))
                        .ToList();

                    // The live price always belongs in the range, even when no point says so yet.
                    if (PriceSeries.Range(urls, entity.Currency, rangeStart, now) is var (min, max))
                    {
                        priceStats[entity.Id] = entity.CurrentPrice is { } current
                            ? (Math.Min(min, current), Math.Max(max, current))
                            : (min, max);
                    }
                }
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
                    : (Count: 0, TargetAlerts: new List<Alert>());
                var worst = ProductAttention.Worst(p.ProductUrls);
                var closest = AlertProximity.Closest(alertSummary.TargetAlerts, p.CurrentPrice, p.Currency);
                // The URL whose price is the product price (the MIN across live URLs in its currency).
                var bestUrl = p.ProductUrls
                    .Where(pu => pu.Status != ProductUrlStatus.Paused && pu.CurrentPrice != null &&
                                 string.Equals(pu.Currency, p.Currency, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(pu => pu.CurrentPrice)
                    .ThenBy(pu => pu.Id)
                    .FirstOrDefault() ?? firstUrl;

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
                    affiliateUrl,
                    p.CheckIntervalMinutes,
                    p.HasPriceAnomaly,
                    worst is (var kind, var worstUrl)
                        ? new AttentionDto(kind.ToApiString(), ProductAttention.IsAction(kind), worstUrl.Id,
                            worstUrl.Url, worstUrl.ConsecutiveFailures, worstUrl.BackoffUntil, worstUrl.LastError)
                        : null,
                    closest is (var alert, var gap, var isNear)
                        ? new AlertTargetDto(alert.Id, alert.Condition.ToApiString(), alert.TargetPrice, gap, isNear)
                        : null,
                    bestUrl?.Url,
                    bestUrl?.StoreId is { } bestStoreId ? storeNames.GetValueOrDefault(bestStoreId) : null,
                    p.ProductUrls.Max(pu => pu.LastSucceededAt),
                    rowValues.GetValueOrDefault(p.Id)?.Change7d is { } change ? Math.Round(change, 2) : null,
                    rowValues.GetValueOrDefault(p.Id)?.TrackedSince
                );
            }).ToList();

            logger.LogDebug("Returning {Count}/{Total} products for user {UserId}", products.Count, total, request.UserId);
            return new Response(products, total, page, pageSize, atLowestCount, priceDropCount, withAlertsCount,
                needsActionCount, nearTargetCount, productCount, urlCount, stores);
        }
    }
}
