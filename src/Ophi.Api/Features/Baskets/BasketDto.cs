using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Common.Helpers;
using Ophi.Domain.Services;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Features.Baskets;

public record BasketItemDto(Guid ProductId, string ProductName, string? ImageUrl, int Quantity);

public record BasketDto(
    Guid Id,
    string Name,
    string? Description,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<BasketItemDto> Items,
    List<BasketStoreDto> Stores,
    string? BestStore,
    BasketSplitDto? Split);

/// <summary>The unit price of an item at a store; null when the store does not sell it.</summary>
public record BasketStoreItemDto(Guid ProductId, decimal? Price, string? Currency);

/// <summary>The basket at one site (<see cref="Domain.Services.BasketTotals.StoreTotal"/>).</summary>
public record BasketStoreDto(
    string Host,
    string? Currency,
    List<BasketStoreItemDto> Items,
    decimal? Subtotal,
    decimal? Shipping,
    decimal? Total,
    bool Complete,
    int MissingCount);

public record BasketSplitLineDto(Guid ProductId, string? Host, decimal? Price);

public record BasketHostShippingDto(string Host, decimal? Shipping);

/// <summary>Each item at its cheapest site (<see cref="Domain.Services.BasketTotals.Split"/>).</summary>
public record BasketSplitDto(
    string Currency,
    List<BasketSplitLineDto> Lines,
    List<BasketHostShippingDto> Shipping,
    decimal? Total,
    bool Complete);

/// <summary>A basket in the list, with its best store's total when it has one (<see cref="BasketDto.BestStore"/>).</summary>
public record BasketSummaryDto(Guid Id, string Name, string? Description, int ItemCount, DateTime UpdatedAt,
    string? BestStore = null, decimal? BestTotal = null, string? BestCurrency = null);

/// <summary>The one read of a basket, shared by every basket slice that answers with a <see cref="BasketDto"/>.</summary>
public static class BasketReader
{
    public static Task<BasketDto> LoadAsync(OphiDbContext dbContext, Guid basketId, Guid userId, CancellationToken cancellationToken) =>
        LoadCoreAsync(dbContext, basketId, userId, cancellationToken);

    private static async Task<BasketDto> LoadCoreAsync(OphiDbContext dbContext, Guid basketId, Guid userId, CancellationToken cancellationToken)
    {
        // Sequential awaits: a DbContext does not allow concurrent operations.
        var basket = await dbContext.Baskets
            .AsNoTracking()
            .Where(b => b.Id == basketId && b.UserId == userId)
            .Select(b => new { b.Id, b.Name, b.Description, b.CreatedAt, b.UpdatedAt })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Basket not found");

        var items = await dbContext.BasketItems
            .AsNoTracking()
            .Where(i => i.BasketId == basketId)
            .OrderBy(i => i.AddedAt)
            .ThenBy(i => i.Product.Name)
            .Select(i => new BasketItemDto(i.ProductId, i.Product.Name, i.Product.ImageUrl, i.Quantity))
            .ToListAsync(cancellationToken);

        // Totals: every URL of the items' products, and the user's shipping rates. BasketTotals owns the rules.
        var productIds = items.Select(i => i.ProductId).ToList();
        var urls = (await dbContext.ProductUrls
                .AsNoTracking()
                .Where(u => productIds.Contains(u.ProductId))
                .ToListAsync(cancellationToken))
            .ToLookup(u => u.ProductId);
        var rateFor = await ShippingRateLookup.LoadAsync(dbContext, userId, cancellationToken);
        var totals = BasketTotals.Compute(
            items.Select(i => new BasketTotals.Item(i.ProductId, i.Quantity, urls[i.ProductId].ToList())).ToList(),
            rateFor);

        return new BasketDto(basket.Id, basket.Name, basket.Description, basket.CreatedAt, basket.UpdatedAt, items,
            totals.Stores.Select(st => new BasketStoreDto(
                st.Host, st.Currency,
                st.Items.Select(i => new BasketStoreItemDto(i.ProductId, i.Price, i.Currency)).ToList(),
                st.Subtotal, st.Shipping, st.Total, st.Complete, st.MissingCount)).ToList(),
            totals.BestStoreHost,
            totals.Split is { } split
                ? new BasketSplitDto(split.Currency,
                    split.Lines.Select(l => new BasketSplitLineDto(l.ProductId, l.Host, l.Price)).ToList(),
                    split.Shipping.Select(h => new BasketHostShippingDto(h.Host, h.Shipping)).ToList(),
                    split.Total, split.Complete)
                : null);
    }
}
