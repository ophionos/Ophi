import type { ProductDetail } from '$lib/api/client';

/**
 * Id of the URL that sets the product's price, for the best-price highlight. Null when fewer than
 * two URLs compete, since a highlight on the only option says nothing.
 *
 * Mirrors `ProductPriceAggregator`: only non-paused URLs in the product's currency compete. Raw
 * prices are not comparable across currencies (EUR 95 is not cheaper than USD 100), and a paused
 * URL's price is frozen and never contributes. The aggregator has already re-anchored
 * `product.currency` onto the currency it chose, so filtering on it matches the headline price.
 */
export function bestPriceUrlId(product: ProductDetail | null): string | null {
	if (!product?.urls) return null;
	const currency = product.currency.toUpperCase();
	const competing = product.urls.filter(
		(u) => u.currentPrice != null && u.status !== 'paused' && u.currency.toUpperCase() === currency
	);
	if (competing.length <= 1) return null;
	return competing.reduce((best, u) => (u.currentPrice! < best.currentPrice! ? u : best)).id;
}
