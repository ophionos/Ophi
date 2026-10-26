using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Common.Helpers;
using Ophi.Api.Features.Products;
using Ophi.Domain.Services;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Comparisons;

public static class GetComparisonGroups
{
    public record Query(Guid UserId);

    public record Response(List<Dto> Items);

    /// <summary>
    /// The card preview: the group's best price, chosen exactly as <see cref="GetComparisonGroup"/>
    /// chooses it, and that product's daily lows over the last <see cref="SparklineDays"/> days.
    /// </summary>
    public record Dto(
        Guid Id,
        string Name,
        int ProductCount,
        Guid? BestPriceProductId,
        string? BestPriceProductName,
        decimal? BestPrice,
        string? Currency,
        List<GetProducts.SparklinePointDto> Sparkline,
        Guid? BestLandedProductId = null,
        string? BestLandedProductName = null,
        decimal? BestLandedPrice = null);

    public const int SparklineDays = 30;

    private sealed record PricedMember(Guid Id, Guid GroupId, string Name, decimal? CurrentPrice, string Currency, decimal? LandedPrice);

    public class Handler(OphiDbContext dbContext, TimeProvider timeProvider, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching comparison groups for user {UserId}", request.UserId);

            var groups = await dbContext.ComparisonGroups
                .Where(cg => cg.UserId == request.UserId)
                .OrderBy(cg => cg.Name)
                .Select(cg => new { cg.Id, cg.Name, ProductCount = cg.Products.Count })
                .ToListAsync(cancellationToken);

            // The cheapest member is chosen in memory: SQLite cannot ORDER BY a decimal, and the
            // shared rule (currency first, then the MIN) is not expressible in SQL anyway.
            var groupIds = groups.Select(g => g.Id).ToList();
            // URLs are loaded for the landed price, which must match GetComparisonGroup's.
            var rateFor = await ShippingRateLookup.LoadAsync(dbContext, request.UserId, cancellationToken);
            var members = (await dbContext.Products
                    .AsNoTracking()
                    .Include(p => p.ProductUrls)
                    .Where(p => p.ComparisonGroupId != null && groupIds.Contains(p.ComparisonGroupId.Value) && p.CurrentPrice != null)
                    .ToListAsync(cancellationToken))
                .Select(p => new PricedMember(p.Id, p.ComparisonGroupId!.Value, p.Name, p.CurrentPrice, p.Currency,
                    LandedPrice.ForProduct(p, rateFor)?.Price))
                .ToList();

            var best = members
                .GroupBy(m => m.GroupId)
                .ToDictionary(
                    g => g.Key,
                    g => ProductPriceAggregator.CheapestInDominantCurrency(g, m => m.CurrentPrice, m => m.Currency, m => m.Id)!);

            var bestLanded = members
                .GroupBy(m => m.GroupId)
                .ToDictionary(
                    g => g.Key,
                    g => ProductPriceAggregator.CheapestInDominantCurrency(g, m => m.LandedPrice, m => m.Currency, m => m.Id));

            var sparklines = await LoadSparklinesAsync(best.Values.ToList(), cancellationToken);

            var items = groups.Select(g =>
            {
                var winner = best.GetValueOrDefault(g.Id);
                var landedWinner = bestLanded.GetValueOrDefault(g.Id);
                return new Dto(
                    g.Id,
                    g.Name,
                    g.ProductCount,
                    winner?.Id,
                    winner?.Name,
                    winner?.CurrentPrice,
                    winner?.Currency,
                    winner != null ? sparklines.GetValueOrDefault(winner.Id, []) : [],
                    landedWinner?.Id,
                    landedWinner?.Name,
                    landedWinner?.LandedPrice);
            }).ToList();

            logger.LogDebug("Returning {Count} comparison groups for user {UserId}", items.Count, request.UserId);
            return new Response(items);
        }

        private async Task<Dictionary<Guid, List<GetProducts.SparklinePointDto>>> LoadSparklinesAsync(
            List<PricedMember> winners, CancellationToken cancellationToken)
        {
            if (winners.Count == 0) return [];

            var ids = winners.Select(w => w.Id).ToList();
            var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-SparklineDays);

            // Points in another currency (a re-pointed URL) cannot be drawn against the product's price.
            var points = await dbContext.PricePoints
                .AsNoTracking()
                .Where(pp => ids.Contains(pp.ProductId) && pp.RecordedAt >= cutoff && pp.Currency == pp.Product.Currency)
                .Select(pp => new { pp.ProductId, pp.Price, pp.RecordedAt })
                .ToListAsync(cancellationToken);

            return points
                .GroupBy(pp => pp.ProductId)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(pp => pp.RecordedAt.Date)
                        .OrderBy(dg => dg.Key)
                        // The day's low, as on the product sparklines (GetProducts).
                        .Select(dg => new GetProducts.SparklinePointDto(
                            dg.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            dg.Min(pp => pp.Price)))
                        .ToList());
        }
    }

    public static void MapGetComparisonGroupsEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/v1/comparisons", async (IMessageBus bus, HttpContext context) =>
        {
            var query = new Query(context.User.GetUserId());
            var result = await bus.InvokeAsync<Response>(query);
            return Results.Ok(result);
        })
        .WithName("GetComparisonGroups")
        .WithTags("Comparisons")
        .WithSummary("List comparison groups")
        .WithDescription("Returns all comparison groups for the current user with the product count of each group, its best price (chosen in the currency most members are priced in) and that product's daily lows over the last 30 days. Get a single group for every member's price. bestLandedProductId/Name/Price name the cheapest price including the user's shipping rates, chosen as on the group page.")
        .Produces<Response>(200)
        .RequireAuthorization();
}
