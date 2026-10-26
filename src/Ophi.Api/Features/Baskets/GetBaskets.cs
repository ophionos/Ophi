using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Extensions;
using Ophi.Api.Common.Helpers;
using Ophi.Domain.Services;
using Ophi.Infrastructure.Persistence;
using Wolverine;

namespace Ophi.Api.Features.Baskets;

public static class GetBaskets
{
    public record Query(Guid UserId);

    public record Response(List<BasketSummaryDto> Items);

    public class Handler(OphiDbContext dbContext, ILogger<Handler> logger)
    {
        public async Task<Response> Handle(Query request, CancellationToken cancellationToken)
        {
            logger.LogDebug("Fetching baskets for user {UserId}", request.UserId);

            var items = await dbContext.Baskets
                .AsNoTracking()
                .Where(b => b.UserId == request.UserId)
                .OrderByDescending(b => b.UpdatedAt)
                .ThenBy(b => b.Name)
                .Select(b => new BasketSummaryDto(b.Id, b.Name, b.Description, b.Items.Count, b.UpdatedAt))
                .ToListAsync(cancellationToken);

            // Each basket's best store, from the same BasketTotals rules as the detail page.
            var basketItems = await dbContext.BasketItems
                .AsNoTracking()
                .Where(i => i.Basket.UserId == request.UserId)
                .Select(i => new { i.BasketId, i.ProductId, i.Quantity })
                .ToListAsync(cancellationToken);
            var productIds = basketItems.Select(i => i.ProductId).Distinct().ToList();
            var urls = (await dbContext.ProductUrls
                    .AsNoTracking()
                    .Where(u => productIds.Contains(u.ProductId))
                    .ToListAsync(cancellationToken))
                .ToLookup(u => u.ProductId);
            var rateFor = await ShippingRateLookup.LoadAsync(dbContext, request.UserId, cancellationToken);
            var itemsByBasket = basketItems.ToLookup(i => i.BasketId);

            return new Response(items.Select(summary =>
            {
                var totals = BasketTotals.Compute(
                    itemsByBasket[summary.Id]
                        .Select(i => new BasketTotals.Item(i.ProductId, i.Quantity, urls[i.ProductId].ToList()))
                        .ToList(),
                    rateFor);
                var best = totals.Stores.FirstOrDefault(st => st.Host == totals.BestStoreHost);
                return best is null
                    ? summary
                    : summary with { BestStore = best.Host, BestTotal = best.Total, BestCurrency = best.Currency };
            }).ToList());
        }
    }

    public static void MapGetBasketsEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/v1/baskets", async (IMessageBus bus, HttpContext context) =>
        {
            var result = await bus.InvokeAsync<Response>(new Query(context.User.GetUserId()));
            return Results.Ok(result);
        })
        .WithName("GetBaskets")
        .WithTags("Baskets")
        .WithSummary("List baskets")
        .WithDescription("Returns the current user's baskets with the number of items in each, most recently updated first, and each basket's best store with its total and currency (null when no complete store has a known total; the rules are the single-basket endpoint's). Get a single basket for its items and every store.")
        .Produces<Response>(200)
        .RequireAuthorization();
}
