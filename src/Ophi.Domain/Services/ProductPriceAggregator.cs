using Ophi.Domain.Entities;

namespace Ophi.Domain.Services;

/// <summary>
/// Owns the cross-entity invariant that a product's headline price is the lowest current price
/// across its <see cref="ProductUrl"/> children. Lives in the Domain layer because the rule
/// (MIN across URLs, with currency tracked alongside) is a domain invariant, not a persistence
/// concern.
///
/// Callers (handlers) load the relevant URL data and invoke <see cref="ApplyAggregate"/>;
/// the method mutates the <see cref="Product"/> in place. It does NOT save — the caller's
/// existing <c>SaveChangesAsync</c> commits the change as part of its broader unit of work.
/// </summary>
public static class ProductPriceAggregator
{
    /// <summary>
    /// Recomputes the product-level price as the MIN across every URL that has a current price
    /// (including the just-updated one) <em>and</em> is denominated in the product's currency.
    /// On a real change the prior <see cref="Product.CurrentPrice"/> is captured into
    /// <see cref="Product.PreviousPrice"/> and <see cref="Product.Currency"/> follows the chosen
    /// min-priced URL. A currency change clears <see cref="Product.PreviousPrice"/> instead. When no URL matches the product's currency the product re-anchors onto the
    /// currency its listings actually use — the dominant one — and takes the MIN within that.
    /// </summary>
    /// <param name="product">Product whose aggregate price is being recomputed.</param>
    /// <param name="urlPrices">
    /// All URLs that contribute to the aggregate. The caller is responsible for including the
    /// just-updated URL in this list (and excluding any URLs with <c>null</c> prices).
    /// </param>
    public static void ApplyAggregate(Product product, IReadOnlyCollection<UrlPrice> urlPrices)
    {
        if (urlPrices.Count == 0) return;

        // Only URLs denominated in the product's own currency compete for the MIN. Raw decimals are
        // not comparable across currencies: EUR 95 beats USD 100 numerically while actually costing
        // more, and letting it win silently re-denominates the product on every scrape — which then
        // flows into alert comparisons, which have no currency guard of their own.
        var comparable = urlPrices
            .Where(u => string.Equals(u.Currency, product.Currency, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Nothing is priced in the product's currency any more (e.g. every URL was re-pointed to
        // another region). Re-anchor onto what is actually tracked rather than leave it priceless —
        // but pick the currency FIRST and take the MIN inside it. Taking the MIN over the whole
        // mixed set would reintroduce exactly the comparison above: GBP 20 is not cheaper than
        // EUR 30. The dominant currency (most contributing URLs) wins, ties broken on the currency
        // code so the result depends on neither list order nor a meaningless decimal comparison.
        if (comparable.Count == 0)
        {
            var anchorCurrency = urlPrices
                .GroupBy(u => u.Currency, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .First().Key;

            comparable = urlPrices
                .Where(u => string.Equals(u.Currency, anchorCurrency, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var min = comparable.OrderBy(u => u.Price).First();

        // PreviousPrice has no currency of its own: it is read as being in product.Currency. On a
        // re-anchor the old price is in another denomination, so no baseline survives — keeping it
        // turned USD 100 -> EUR 92 into a fake 8% drop on the dashboard and the webhook OldPrice.
        if (!string.Equals(min.Currency, product.Currency, StringComparison.OrdinalIgnoreCase))
        {
            product.PreviousPrice = null;
        }
        // Only capture into PreviousPrice on a real change. A no-op recheck (new MIN == current)
        // must not overwrite PreviousPrice, or downstream readers (dashboard % change, price-change
        // sort, alert webhook OldPrice) silently zero out after the first quiet scrape.
        else if (product.CurrentPrice.HasValue && product.CurrentPrice.Value != min.Price)
        {
            product.PreviousPrice = product.CurrentPrice;
        }
        product.CurrentPrice = min.Price;
        product.Currency = min.Currency;
    }

    /// <summary>
    /// Like <see cref="ApplyAggregate"/>, but for a caller that passes the COMPLETE set of priced
    /// live URLs after one left it (removed or paused). There an empty set means no live URL has a
    /// price, so the product price is cleared. <see cref="ApplyAggregate"/> keeps its no-op on an
    /// empty set so a scrape that found nothing cannot clobber a good price.
    /// </summary>
    public static void ApplyLiveAggregate(Product product, IReadOnlyCollection<UrlPrice> livePrices)
    {
        if (livePrices.Count == 0)
            product.CurrentPrice = null;
        else
            ApplyAggregate(product, livePrices);
    }

    /// <summary>
    /// Lightweight payload for <see cref="ApplyAggregate"/>. Carries just enough of a
    /// <see cref="ProductUrl"/> to compute the min — avoids forcing callers to materialize the
    /// full entity when an EF projection works.
    /// </summary>
    public readonly record struct UrlPrice(decimal Price, string Currency);
}
