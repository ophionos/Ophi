import type { ProductDetail } from '$lib/api/client';

/**
 * Whether the product detail page should keep polling for fresh scrape results.
 *
 * True when the product itself is still `pending`, or when any tracked URL is awaiting its
 * first scrape — no price yet, never checked, and not parked in an error/paused state. Once a
 * URL has been checked (`lastCheckedAt` set) it stops qualifying regardless of outcome, which
 * naturally bounds polling so a persistently failing URL can't keep the page polling forever.
 */
export function isAwaitingScrape(product: ProductDetail | null): boolean {
	if (!product) return false;
	if (product.status === 'pending') return true;
	return (product.urls ?? []).some(
		(u) =>
			u.currentPrice == null &&
			u.lastCheckedAt == null &&
			u.status !== 'paused' &&
			u.status !== 'error'
	);
}
