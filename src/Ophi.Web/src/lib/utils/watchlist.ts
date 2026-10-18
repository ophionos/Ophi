// Display rules for the dashboard watchlist (table and gallery), kept out of the components so
// both views word and judge a product the same way.
import type { AlertTarget, AttentionKind, Product } from '$lib/api/client';
import { formatPrice } from '$lib/format';

/**
 * A price whose last successful read is older than this is shown as an old price. The column
 * cannot use `lastChecked`: a failed check moves it too.
 */
export const STALE_AFTER_HOURS = 48;

const ATTENTION_LABELS: Record<AttentionKind, string> = {
	blocked: 'Blocked',
	priceNotFound: 'Price not found',
	failing: 'Failing',
	priceAnomaly: 'Check price',
	outOfStock: 'Out of stock',
	paused: 'Paused by you'
};

export function attentionLabel(kind: AttentionKind): string {
	return ATTENTION_LABELS[kind];
}

/** The store that gives the current price: its configured name, else the URL's host. */
export function storeLabel(product: Product): string {
	if (product.bestStoreName) return product.bestStoreName;
	try {
		return new URL(product.bestUrl ?? product.url).hostname.replace(/^www\./, '');
	} catch {
		return '';
	}
}

/** Null `lastSucceededAt` (a row from before the column) is not judged old: no false alarms. */
export function isPriceOld(product: Product, now: Date = new Date()): boolean {
	if (product.currentPrice == null || !product.lastSucceededAt) return false;
	const age = now.getTime() - new Date(product.lastSucceededAt).getTime();
	return age > STALE_AFTER_HOURS * 3_600_000;
}

export function alertTargetText(target: AlertTarget, currency: string): string {
	return `${target.condition === 'below' ? '≤' : '≥'} ${formatPrice(target.targetPrice, currency)}`;
}

/** The API's gap is zero or less once the target is reached. */
export function alertDistanceText(target: AlertTarget, currency: string): string {
	return target.gap <= 0 ? 'Reached' : `${formatPrice(target.gap, currency)} away`;
}

/** Stands in for a missing image: the first letter or digit of the name. */
export function productInitial(name: string): string {
	const match = name.match(/[\p{L}\p{N}]/u);
	return match ? match[0].toUpperCase() : '?';
}
