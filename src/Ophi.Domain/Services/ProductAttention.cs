using System.Linq.Expressions;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;

namespace Ophi.Domain.Services;

/// <summary>
/// What a product's URLs need from the user, worst first. The declaration order IS the severity
/// order: <see cref="ProductAttention.Worst"/> picks the lowest value.
/// </summary>
public enum AttentionKind
{
    /// <summary>The store refuses automated requests (anti-bot), including a URL auto-paused for it.</summary>
    Blocked,
    /// <summary>The page loaded but no price could be read from it.</summary>
    PriceNotFound,
    /// <summary>Any other failure, repeated at least <see cref="ProductAttention.FailingAfter"/> times.</summary>
    Failing,
    /// <summary>The last price looked like an anomaly; it was stored, so the user should check it.</summary>
    PriceAnomaly,
    /// <summary>Info: the store reports the product unavailable.</summary>
    OutOfStock,
    /// <summary>Info: the user paused the URL.</summary>
    Paused
}

/// <summary>
/// The one rule for "does this product need the user's attention, and why". The dashboard row,
/// its action card and its count all read it, so they cannot disagree.
/// </summary>
public static class ProductAttention
{
    /// <summary>
    /// A generic failure counts once it repeats: one 500 is noise, two in a row is when
    /// <see cref="ProductUrl.BackoffUntil"/> starts too. Blocked and price-not-found count at once.
    /// </summary>
    public const int FailingAfter = 2;

    /// <summary>States the user can act on. Out of stock and paused-by-you are information only.</summary>
    public static bool IsAction(AttentionKind kind) => kind <= AttentionKind.PriceAnomaly;

    /// <summary>
    /// One URL's state. Only anti-bot blocks auto-pause a URL, and the pause keeps the error, so a
    /// paused URL whose last error is anti-bot was paused by the worker and still needs the user.
    /// A rate limit retries by itself and never needs the user.
    /// </summary>
    public static AttentionKind? Classify(ProductUrl url)
    {
        if (url.Status == ProductUrlStatus.Paused)
            return url.LastErrorCategory == ScrapeErrorCategory.AntiBot ? AttentionKind.Blocked : AttentionKind.Paused;

        if (url.LastError != null)
        {
            if (url.LastErrorCategory == ScrapeErrorCategory.AntiBot) return AttentionKind.Blocked;
            if (url.LastErrorCategory == ScrapeErrorCategory.ParseError) return AttentionKind.PriceNotFound;
            if (url.LastErrorCategory != ScrapeErrorCategory.RateLimited && url.ConsecutiveFailures >= FailingAfter)
                return AttentionKind.Failing;
        }

        if (url.HasPriceAnomaly) return AttentionKind.PriceAnomaly;
        if (url.IsOutOfStock) return AttentionKind.OutOfStock;
        return null;
    }

    /// <summary>
    /// The product's worst state and the URL to act on. An action state on any URL wins. An info
    /// state counts only when every URL has one: a single store out of stock or paused is not news
    /// while another store still tracks the price. Ties break on the URL id.
    /// </summary>
    public static (AttentionKind Kind, ProductUrl Url)? Worst(IEnumerable<ProductUrl> urls)
    {
        var classified = urls.Select(u => (Kind: Classify(u), Url: u)).ToList();
        if (classified.Count == 0) return null;

        var candidates = classified.Any(c => c.Kind is { } k && IsAction(k))
            ? classified.Where(c => c.Kind is { } k && IsAction(k))
            : classified.All(c => c.Kind != null) ? classified : [];

        return candidates
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.Url.Id)
            .Select(c => ((AttentionKind Kind, ProductUrl Url)?)(c.Kind!.Value, c.Url))
            .FirstOrDefault();
    }

    /// <summary>
    /// <see cref="Classify"/>'s action states as a query predicate, for the account-wide count and
    /// the "needs action" filter. It restates the rule because EF cannot translate a method call;
    /// ProductAttentionTests runs both over the same cases.
    /// </summary>
    public static readonly Expression<Func<Product, bool>> NeedsAction = p => p.ProductUrls.Any(u =>
        (u.Status == ProductUrlStatus.Paused && u.LastErrorCategory == ScrapeErrorCategory.AntiBot) ||
        (u.Status != ProductUrlStatus.Paused && (
            (u.LastError != null && (
                u.LastErrorCategory == ScrapeErrorCategory.AntiBot ||
                u.LastErrorCategory == ScrapeErrorCategory.ParseError ||
                ((u.LastErrorCategory == null || u.LastErrorCategory != ScrapeErrorCategory.RateLimited) &&
                 u.ConsecutiveFailures >= FailingAfter))) ||
            u.HasPriceAnomaly)));
}
