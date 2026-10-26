using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Extensions;
using Ophi.Domain.Enums;
using Ophi.Domain.Services;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Baskets;

public record BasketHistoryPointDto(DateTime Date, decimal Total);

public record BasketHistorySeriesDto(string Host, string Currency, List<BasketHistoryPointDto> Points);

public record BasketHistoryDto(int Days, List<BasketHistorySeriesDto> Series);

public static class GetBasketHistory
{
    public record Query(Guid Id, Guid UserId, int Days = 30);

    public class Handler(OphiDbContext dbContext, TimeProvider timeProvider)
    {
        public async Task<BasketHistoryDto> Handle(Query request, CancellationToken cancellationToken)
        {
            // Sequential awaits: a DbContext does not allow concurrent operations.
            var owned = await dbContext.Baskets
                .AnyAsync(b => b.Id == request.Id && b.UserId == request.UserId, cancellationToken);
            if (!owned) throw new NotFoundException("Basket not found");

            var items = await dbContext.BasketItems
                .AsNoTracking()
                .Where(i => i.BasketId == request.Id)
                .Select(i => new { i.ProductId, i.Quantity })
                .ToListAsync(cancellationToken);
            var productIds = items.Select(i => i.ProductId).ToList();
            var urls = await dbContext.ProductUrls
                .AsNoTracking()
                .Where(u => productIds.Contains(u.ProductId))
                .ToListAsync(cancellationToken);
            var rateFor = await ShippingRateLookup.LoadAsync(dbContext, request.UserId, cancellationToken);

            // One series per store that is complete now, in the order of the live totals.
            var live = BasketTotals.Compute(
                items.Select(i => new BasketTotals.Item(i.ProductId, i.Quantity, urls.Where(u => u.ProductId == i.ProductId).ToList())).ToList(),
                rateFor);
            var hosts = live.Stores.Where(s => s.Complete).Select(s => s.Host).ToList();

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var start = now.Date.AddDays(-request.Days);
            var urlIds = urls.Select(u => (Guid?)u.Id).ToList();
            var inWindow = (await dbContext.PricePoints
                    .AsNoTracking()
                    .Where(pp => urlIds.Contains(pp.ProductUrlId) && pp.RecordedAt >= start)
                    .OrderBy(pp => pp.RecordedAt)
                    .Select(pp => new { pp.ProductUrlId, pp.RecordedAt, pp.Price, pp.Currency })
                    .ToListAsync(cancellationToken))
                .ToLookup(pp => pp.ProductUrlId!.Value);

            // The held-start rule of the product history (GetPriceHistory, #134): a URL's price from
            // before the window holds from the window start, unless the URL is no longer scraped and
            // has no change in the window. One small query per URL, as there: GroupBy(...).First()
            // does not translate on every provider.
            var histories = new Dictionary<Guid, BasketHistory.UrlHistory>();
            foreach (var url in urls)
            {
                var reachesNow = url.Status != ProductUrlStatus.Paused && !url.IsOutOfStock;
                var changes = inWindow[url.Id].Select(pp => new BasketHistory.Price(pp.RecordedAt, pp.Price, pp.Currency)).ToList();
                var points = new List<BasketHistory.Price>();
                if (reachesNow || changes.Count > 0)
                {
                    var prior = await dbContext.PricePoints
                        .AsNoTracking()
                        .Where(pp => pp.ProductUrlId == url.Id && pp.RecordedAt < start)
                        .OrderByDescending(pp => pp.RecordedAt)
                        .Select(pp => new { pp.Price, pp.Currency })
                        .FirstOrDefaultAsync(cancellationToken);
                    if (prior != null) points.Add(new BasketHistory.Price(start, prior.Price, prior.Currency));
                    points.AddRange(changes);
                }
                histories[url.Id] = new BasketHistory.UrlHistory(url.Url, points, reachesNow);
            }

            var series = BasketHistory.Compute(
                items.Select(i => new BasketHistory.Item(i.ProductId, i.Quantity,
                    urls.Where(u => u.ProductId == i.ProductId).Select(u => histories[u.Id]).ToList())).ToList(),
                hosts, rateFor, now);

            return new BasketHistoryDto(request.Days, series
                .Select(s => new BasketHistorySeriesDto(s.Host, s.Currency,
                    s.Points.Select(p => new BasketHistoryPointDto(p.At, p.Total)).ToList()))
                .ToList());
        }
    }

    public static void MapGetBasketHistoryEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/v1/baskets/{id:guid}/history", async (Guid id, int? days, IMessageBus bus, HttpContext context) =>
        {
            var query = new Query(id, context.User.GetUserId(), Math.Clamp(days ?? 30, 1, 365));
            var result = await bus.InvokeAsync<BasketHistoryDto>(query);
            return Results.Ok(result);
        })
        .WithName("GetBasketHistory")
        .WithTags("Baskets")
        .WithSummary("Get a basket's total over time")
        .WithDescription("Returns the basket total per store over the last `days` days (default 30, max 365): one series for each store (site) that is complete now, in the order of the live totals. At each price change of an item URL on that site, every URL holds its last price, and the total is the cheapest price of each item there times its quantity, plus shipping once from the user's current rate (a free-above threshold applies to the subtotal at that instant). A series starts at the first instant where the site has every item, a price from before the window holds from the window start, and the last point is at the request time. Shipping rates are not versioned, so the current rate applies to the whole window; out-of-stock periods are not in the price history, so they do not show. No currency conversion. Returns 404 for a basket that belongs to another user.")
        .Produces<BasketHistoryDto>(200)
        .RequireAuthorization();
}
