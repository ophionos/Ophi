using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Products;

public static class GetPriceHistory
{
    public static void MapGetPriceHistoryEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/products/{id:guid}/history", async (Guid id, int? days, IMessageBus bus, HttpContext context) =>
        {
            var query = new Query(id, context.User.GetUserId(), Math.Clamp(days ?? 30, 1, 365));
            var result = await bus.InvokeAsync<Response>(query);
            return Results.Ok(result);
        })
        .WithName("GetPriceHistory")
        .WithTags("Products")
        .WithSummary("Get price history for a product")
        .WithDescription("Returns chronological price points for a product. Supports a configurable number of days (default 30, max 365). Points are price changes: the first point is the price held at the window start, and a URL that is still scraped ends with its current price at the request time.")
        .Produces<Response>(200)
        .RequireAuthorization();
    }

    public record Query(Guid ProductId, Guid UserId, int Days = 30);

    public record Response(
         Guid ProductId,
        string ProductName,
        List<HistoryPointDto> History,
        StatisticsDto Statistics,
        List<UrlHistoryDto>? UrlHistories,
        bool HasCurrencyMismatch
    );

    public record HistoryPointDto(DateTime Date, decimal Price);

    public record StatisticsDto(decimal Min, decimal Max, decimal Average, decimal? Current);

    public record UrlHistoryDto(Guid ProductUrlId, string Url, string Currency, List<HistoryPointDto> History);

    public class Handler(OphiDbContext dbContext, TimeProvider timeProvider, ILogger<Handler> logger)
    {
        // Price history is change-only: RecordPriceHistoryHandler skips a scrape whose price
        // matches the URL's last point. So a series is the price held before the window (drawn
        // from the window start), the changes inside the window, and — while the URL is still
        // scraped — the last price held up to now. A series never starts before its first known
        // price: a URL first scraped inside the window is not backfilled (#134).
        private static List<HistoryPointDto> BuildSeries(
            decimal? heldBefore, List<HistoryPointDto> inWindow, bool reachesNow, DateTime startDate)
        {
            // A paused or out-of-stock URL is not scraped (or has no price), so nothing says a
            // price from before the window still held during it.
            if (!reachesNow && inWindow.Count == 0) return [];

            var series = new List<HistoryPointDto>();
            if (heldBefore is { } held) series.Add(new HistoryPointDto(startDate, held));
            series.AddRange(inWindow);
            return series;
        }

        // Only a URL that is still scraped holds its last price up to now; a paused or
        // out-of-stock one ends at its last point.
        private static List<HistoryPointDto> ExtendToNow(List<HistoryPointDto> series, bool reachesNow, DateTime now) =>
            reachesNow && series.Count > 0 && series[^1].Date < now
                ? [.. series, new HistoryPointDto(now, series[^1].Price)]
                : series;

        private static bool ReachesNow(ProductUrl url) => url.Status != ProductUrlStatus.Paused && !url.IsOutOfStock;

        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching price history for product {ProductId} ({Days} days)", request.ProductId, request.Days);

            var product = await dbContext.Products
                .Include(p => p.ProductUrls)
                .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.UserId == request.UserId, cancellationToken) ?? throw new NotFoundException("Product not found");
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var startDate = now.Date.AddDays(-request.Days);

            var rawPoints = await dbContext.PricePoints
                .Where(pp => pp.ProductId == request.ProductId && pp.RecordedAt >= startDate)
                .OrderBy(pp => pp.RecordedAt)
                .Select(pp => new { pp.ProductUrlId, pp.RecordedAt, pp.Price, pp.Currency })
                .ToListAsync(cancellationToken);

            // On short windows (<= 30 days) keep intraday granularity: a product scraped several
            // times a day should show several points, not collapse to one. On long windows
            // deduplicate to one entry per URL per day (keep the latest scrape) so the series
            // stays readable. RecordedAt is preserved (not truncated to midnight) on short windows
            // so the chart can show time-of-day.
            var bucketByDay = request.Days > 30;
            var allPoints = bucketByDay
                ? rawPoints
                    .GroupBy(pp => new { pp.ProductUrlId, Date = pp.RecordedAt.Date })
                    .Select(g => g.Last())
                    .OrderBy(pp => pp.RecordedAt)
                    .Select(pp => new { pp.ProductUrlId, RecordedAt = pp.RecordedAt.Date, pp.Price, pp.Currency })
                    .ToList()
                : rawPoints;

            var productHeldBefore = await dbContext.PricePoints
                .Where(pp => pp.ProductId == request.ProductId && pp.RecordedAt < startDate)
                .OrderByDescending(pp => pp.RecordedAt)
                .Select(pp => (decimal?)pp.Price)
                .FirstOrDefaultAsync(cancellationToken);

            var inWindow = allPoints.Select(pp => new HistoryPointDto(pp.RecordedAt, pp.Price)).ToList();
            var productReachesNow = product.ProductUrls.Any(ReachesNow);
            var recorded = BuildSeries(productHeldBefore, inWindow, productReachesNow, startDate);
            var history = ExtendToNow(recorded, productReachesNow, now);

            // Statistics describe recorded prices, so they leave out the point added at now.
            var statistics = recorded.Count > 0
                ? new StatisticsDto(
                    recorded.Min(h => h.Price),
                    recorded.Max(h => h.Price),
                    Math.Round(recorded.Average(h => h.Price), 2),
                    product.CurrentPrice
                )
                : new StatisticsDto(0, 0, 0, product.CurrentPrice);

            // Build per-URL histories when there are multiple URLs
            List<UrlHistoryDto>? urlHistories = null;
            if (product.ProductUrls.Count > 1)
            {
                // Each URL's latest point before the window. One small query per URL: a product
                // has a handful of URLs, and GroupBy(...).First() does not translate on every
                // provider (SQLite test tier vs production Postgres).
                var heldBefore = new Dictionary<Guid, (decimal Price, string Currency)>();
                foreach (var pu in product.ProductUrls)
                {
                    var prior = await dbContext.PricePoints
                        .Where(pp => pp.ProductUrlId == pu.Id && pp.RecordedAt < startDate)
                        .OrderByDescending(pp => pp.RecordedAt)
                        .Select(pp => new { pp.Price, pp.Currency })
                        .FirstOrDefaultAsync(cancellationToken);
                    if (prior != null) heldBefore[pu.Id] = (prior.Price, prior.Currency);
                }

                urlHistories = product.ProductUrls
                    .Select(pu =>
                    {
                        var urlPoints = allPoints.Where(pp => pp.ProductUrlId == pu.Id).ToList();
                        var hasPrior = heldBefore.TryGetValue(pu.Id, out var held);
                        // Derive currency from the most recent price point, fall back to ProductUrl.Currency
                        var currency = urlPoints.LastOrDefault()?.Currency ?? (hasPrior ? held.Currency : pu.Currency);
                        var urlHistory = ExtendToNow(
                            BuildSeries(
                                hasPrior ? held.Price : null,
                                urlPoints.Select(pp => new HistoryPointDto(pp.RecordedAt, pp.Price)).ToList(),
                                ReachesNow(pu), startDate),
                            ReachesNow(pu), now);
                        return new UrlHistoryDto(pu.Id, pu.Url, currency, urlHistory);
                    })
                    .ToList();
            }

            // Detect currency mismatch using actual price-point currencies (fall back to ProductUrl.Currency)
            var urlCurrencies = product.ProductUrls
                .Select(pu => allPoints.LastOrDefault(pp => pp.ProductUrlId == pu.Id)?.Currency ?? pu.Currency)
                .Distinct()
                .Count();
            var hasCurrencyMismatch = urlCurrencies > 1;

            logger.LogDebug("Returning {PointCount} price history points for product {ProductId}", history.Count, product.Id);

            return new Response(
                product.Id,
                product.Name,
                history,
                statistics,
                urlHistories,
                hasCurrencyMismatch
            );
        }
    }
}
