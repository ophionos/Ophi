using Microsoft.EntityFrameworkCore;
using Ophi.Infrastructure.Persistence;
using Ophi.Infrastructure.Scraping;

namespace Ophi.Api.Common.Helpers;

/// <summary>
/// A user's tracked URLs, keyed by <see cref="ProductUrlKey"/>. Every "is this URL already tracked?"
/// check goes through it — AddProduct, AddProductUrl, both imports, and the lookup — so they cannot
/// drift apart on what counts as the same URL.
///
/// The keys are computed in memory rather than stored, so URLs saved before the key existed match
/// without a backfill. A self-hosted account tracks at most a few thousand URLs.
/// </summary>
public sealed class TrackedUrlIndex
{
    public sealed record Match(Guid ProductId, Guid ProductUrlId);

    private readonly Dictionary<string, Match> _byKey;

    private TrackedUrlIndex(Dictionary<string, Match> byKey) => _byKey = byKey;

    public static async Task<TrackedUrlIndex> LoadAsync(
        OphiDbContext dbContext, Guid userId, CancellationToken cancellationToken)
    {
        var urls = await dbContext.ProductUrls
            .Where(pu => pu.Product.UserId == userId)
            .Select(pu => new { pu.Id, pu.ProductId, pu.Url })
            .ToListAsync(cancellationToken);

        var byKey = new Dictionary<string, Match>(StringComparer.Ordinal);
        foreach (var u in urls)
            byKey.TryAdd(ProductUrlKey.For(u.Url), new Match(u.ProductId, u.Id));

        return new TrackedUrlIndex(byKey);
    }

    public Match? Find(string url) => _byKey.GetValueOrDefault(ProductUrlKey.For(url));

    public bool Contains(string url) => _byKey.ContainsKey(ProductUrlKey.For(url));

    /// <summary>Records a URL added during the same operation, so a later row in a batch matches it.</summary>
    public void Add(string url, Guid productId, Guid productUrlId) =>
        _byKey.TryAdd(ProductUrlKey.For(url), new Match(productId, productUrlId));
}
