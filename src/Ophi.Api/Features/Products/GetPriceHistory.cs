using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
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
        .WithDescription("Returns chronological price points for a product. Supports a configurable number of days (default 30, max 365). Each point includes the price, currency, and timestamp.")
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
        // A single point can't be drawn as a line (the chart needs >= 2 points). Synthesize a
        // matching point at the window edge so a stable price renders a flat line. Empty series
        // are left empty (no data == nothing to draw).
        private static List<HistoryPointDto> EnsureLineDrawable(List<HistoryPointDto> points, DateTime startDate, DateTime today)
        {
            if (points.Count != 1) return points;
            var only = points[0];
            return only.Date > startDate
                ? [new HistoryPointDto(startDate, only.Price), only]
                : [only, new HistoryPointDto(today, only.Price)];
        }

        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching price history for product {ProductId} ({Days} days)", request.ProductId, request.Days);

            var product = await dbContext.Products
                .Include(p => p.ProductUrls)
                .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.UserId == request.UserId, cancellationToken) ?? throw new NotFoundException("Product not found");
            var today = timeProvider.GetUtcNow().UtcDateTime.Date;
            var startDate = today.AddDays(-request.Days);

            // Single query — fetch raw points with full timestamp so we can deduplicate by date
            var rawPoints = await dbContext.PricePoints
                .Where(pp => pp.ProductId == request.ProductId && pp.RecordedAt >= startDate)
                .OrderBy(pp => pp.RecordedAt)
                .Select(pp => new { pp.ProductUrlId, pp.RecordedAt, pp.Price, pp.Currency })
                .ToListAsync(cancellationToken);

            // If no points in the window, carry forward the most recent point before the window
            // so the chart shows the current stable price instead of appearing empty
            if (rawPoints.Count == 0)
            {
                var lastBefore = await dbContext.PricePoints
                    .Where(pp => pp.ProductId == request.ProductId && pp.RecordedAt < startDate)
                    .OrderByDescending(pp => pp.RecordedAt)
                    .Select(pp => new { pp.ProductUrlId, pp.RecordedAt, pp.Price, pp.Currency })
                    .FirstOrDefaultAsync(cancellationToken);

                if (lastBefore != null)
                {
                    // Synthesize two points: one at window start and one at today, so chart draws a line
                    rawPoints =
                    [
                        new { lastBefore.ProductUrlId, RecordedAt = startDate, lastBefore.Price, lastBefore.Currency },
                        new { lastBefore.ProductUrlId, RecordedAt = today, lastBefore.Price, lastBefore.Currency }
                    ];
                }
            }

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
                : rawPoints
                    .Select(pp => new { pp.ProductUrlId, pp.RecordedAt, pp.Price, pp.Currency })
                    .ToList();

            // A single distinct point can't be drawn as a line (the chart needs >= 2 points),
            // so a stable-price product scraped within the window would render as "not enough
            // data" despite having a known price. Synthesize a matching point at the window edge
            // to draw a flat line — mirrors the rawPoints.Count == 0 carry-forward above.
            var history = EnsureLineDrawable(
                allPoints.Select(pp => new HistoryPointDto(pp.RecordedAt, pp.Price)).ToList(),
                startDate, today);

            var statistics = history.Count > 0
                ? new StatisticsDto(
                    history.Min(h => h.Price),
                    history.Max(h => h.Price),
                    Math.Round(history.Average(h => h.Price), 2),
                    product.CurrentPrice
                )
                : new StatisticsDto(0, 0, 0, product.CurrentPrice);

            // Build per-URL histories when there are multiple URLs
            List<UrlHistoryDto>? urlHistories = null;
            if (product.ProductUrls.Count > 1)
            {
                // Price history is change-only — RecordPriceHistoryHandler skips a scrape whose
                // price matches that URL's last recorded point. So a URL with NO points inside the
                // window means "this price held for the whole window", not "no data": reconstruct it
                // from the URL's latest point BEFORE the window. Without this the store came back
                // with an empty series — invisible in the chart yet still holding a legend entry —
                // and when the only drawable series left was constant, Chart.js expanded the y-axis
                // to ±5% of that single value (see its `min === max` branch), so the missing store's
                // real price ended up BELOW the axis floor. The product-level carry-forward above
                // cannot cover this: it only fires when every URL is missing from the window.
                var idsMissingFromWindow = product.ProductUrls
                    .Where(pu => !allPoints.Any(pp => pp.ProductUrlId == pu.Id))
                    .Select(pu => pu.Id)
                    .ToList();

                var heldPrices = new Dictionary<Guid, (decimal Price, string Currency)>();
                if (idsMissingFromWindow.Count > 0)
                {
                    // Reduced in memory rather than via GroupBy(...).First(), which does not
                    // translate on every provider (SQLite test tier vs production Postgres).
                    var priorPoints = await dbContext.PricePoints
                        .Where(pp => pp.ProductUrlId != null
                            && idsMissingFromWindow.Contains(pp.ProductUrlId.Value)
                            && pp.RecordedAt < startDate)
                        .OrderByDescending(pp => pp.RecordedAt)
                        .Select(pp => new { UrlId = pp.ProductUrlId!.Value, pp.Price, pp.Currency })
                        .ToListAsync(cancellationToken);

                    // Ordered newest-first, so the first entry per URL is the price it still holds.
                    foreach (var pp in priorPoints)
                    {
                        heldPrices.TryAdd(pp.UrlId, (pp.Price, pp.Currency));
                    }
                }

                urlHistories = product.ProductUrls
                    .Select(pu =>
                    {
                        var urlPoints = allPoints.Where(pp => pp.ProductUrlId == pu.Id).ToList();

                        // Nothing in the window, but a known prior price: flat line across the window.
                        // A URL with no points at all is deliberately left empty — there is no held
                        // price to reconstruct, and inventing one would be fabrication.
                        if (urlPoints.Count == 0 && heldPrices.TryGetValue(pu.Id, out var held))
                        {
                            return new UrlHistoryDto(pu.Id, pu.Url, held.Currency,
                            [
                                new HistoryPointDto(startDate, held.Price),
                                new HistoryPointDto(today, held.Price)
                            ]);
                        }

                        // Derive currency from the most recent price point, fall back to ProductUrl.Currency
                        var currency = urlPoints.LastOrDefault()?.Currency ?? pu.Currency;
                        // Apply the same single-point flat-line synthesis per URL so a stable-price
                        // store renders a line instead of an empty series in the multi-line chart.
                        var urlHistory = EnsureLineDrawable(
                            urlPoints.Select(pp => new HistoryPointDto(pp.RecordedAt, pp.Price)).ToList(),
                            startDate, today);
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
