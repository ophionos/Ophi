using Ophi.Domain.Entities;

namespace Ophi.Worker.Handlers;

/// <summary>
/// Builds <see cref="ScrapeLog"/> entries for the scrape handlers. Centralizes the otherwise
/// duplicated <c>new ScrapeLog { ... }</c> constructions in <see cref="ScrapeNewProductHandler"/>
/// and <see cref="CheckProductPriceHandler"/>.
/// </summary>
internal static class ScrapeOutcomeWriter
{
    public static ScrapeLog BuildSuccessLog(
        Guid productId,
        Guid productUrlId,
        decimal? price,
        long durationMs,
        string storeDomain,
        bool isOutOfStock = false) => new()
    {
        Id = Guid.NewGuid(),
        ProductId = productId,
        ProductUrlId = productUrlId,
        Success = true,
        Price = price,
        IsOutOfStock = isOutOfStock,
        DurationMs = (int)durationMs,
        StoreDomain = storeDomain
    };

    public static ScrapeLog BuildFailureLog(
        Guid productId,
        Guid productUrlId,
        string? error,
        long durationMs,
        string storeDomain) => new()
    {
        Id = Guid.NewGuid(),
        ProductId = productId,
        ProductUrlId = productUrlId,
        Success = false,
        Error = error,
        DurationMs = (int)durationMs,
        StoreDomain = storeDomain
    };
}
