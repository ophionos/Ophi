using Microsoft.EntityFrameworkCore;
using Ophi.Api.Common.Exceptions;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Features.Baskets;

public record BasketItemDto(Guid ProductId, string ProductName, string? ImageUrl, int Quantity);

public record BasketDto(
    Guid Id,
    string Name,
    string? Description,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<BasketItemDto> Items);

public record BasketSummaryDto(Guid Id, string Name, string? Description, int ItemCount, DateTime UpdatedAt);

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

        return new BasketDto(basket.Id, basket.Name, basket.Description, basket.CreatedAt, basket.UpdatedAt, items);
    }
}
