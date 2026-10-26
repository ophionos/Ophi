using Microsoft.EntityFrameworkCore;
using Ophi.Domain.Services;

namespace Ophi.Infrastructure.Persistence;

/// <summary>
/// Loads what <see cref="BasketTotals"/> needs for one basket: every URL of the items' products and
/// the user's shipping rates. The API (basket reads, basket alerts) and the worker (the alert check)
/// share it, so both see the same totals. BasketTotals owns the rules.
/// </summary>
public static class BasketTotalsReader
{
    public static async Task<BasketTotals.Result> ComputeAsync(
        OphiDbContext dbContext, Guid basketId, Guid userId, CancellationToken cancellationToken)
    {
        var items = await dbContext.BasketItems
            .AsNoTracking()
            .Where(i => i.BasketId == basketId)
            .Select(i => new { i.ProductId, i.Quantity })
            .ToListAsync(cancellationToken);
        return await ComputeAsync(dbContext, userId, items.Select(i => (i.ProductId, i.Quantity)).ToList(), cancellationToken);
    }

    public static async Task<BasketTotals.Result> ComputeAsync(
        OphiDbContext dbContext, Guid userId, IReadOnlyList<(Guid ProductId, int Quantity)> items, CancellationToken cancellationToken)
    {
        // Sequential awaits: a DbContext does not allow concurrent operations.
        var productIds = items.Select(i => i.ProductId).ToList();
        var urls = (await dbContext.ProductUrls
                .AsNoTracking()
                .Where(u => productIds.Contains(u.ProductId))
                .ToListAsync(cancellationToken))
            .ToLookup(u => u.ProductId);
        var rateFor = await ShippingRateLookup.LoadAsync(dbContext, userId, cancellationToken);
        return BasketTotals.Compute(
            items.Select(i => new BasketTotals.Item(i.ProductId, i.Quantity, urls[i.ProductId].ToList())).ToList(),
            rateFor);
    }
}
