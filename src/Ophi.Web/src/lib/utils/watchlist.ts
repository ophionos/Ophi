// Display rules for the dashboard watchlist (table and gallery), kept out of the components so
// both views word and judge a product the same way.
import type { AlertTarget, AttentionKind, Product, ProductAttention } from '$lib/api/client';
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
	return product.bestStoreName || hostOf(product.bestUrl ?? product.url);
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

export type FixAction =
	| { kind: 'challenge'; label: string }
	| { kind: 'retry'; label: string }
	| { kind: 'link'; label: string };

/**
 * The one fix offered for a problem; the action card and the phone rows both use it. A retry
 * clears the error and scrapes at once, and it also resumes a URL the worker paused for anti-bot.
 * A parse failure or an anomaly needs the product page: a retry would read the same page again.
 */
export function fixAction(kind: AttentionKind, challengeAvailable: boolean): FixAction {
	switch (kind) {
		case 'blocked':
			return challengeAvailable
				? { kind: 'challenge', label: 'Solve check' }
				: { kind: 'retry', label: 'Retry now' };
		case 'failing':
			return { kind: 'retry', label: 'Retry now' };
		case 'priceNotFound':
			return { kind: 'link', label: 'Open product' };
		default:
			return { kind: 'link', label: 'Review' };
	}
}

/** The cause in a few words, for the action card line. */
export function attentionCause(attention: ProductAttention): string {
	switch (attention.kind) {
		case 'blocked':
			return 'The store blocks automated checks';
		case 'priceNotFound':
			return 'No price found on the page';
		case 'failing':
			return `${attention.consecutiveFailures} failed checks in a row`;
		case 'priceAnomaly':
			return 'The last price looks unusual';
		case 'outOfStock':
			return 'Out of stock';
		case 'paused':
			return 'Paused by you';
	}
}

/** The host of the URL to act on, without "www.". */
export function hostOf(url: string): string {
	try {
		return new URL(url).hostname.replace(/^www\./, '');
	} catch {
		return '';
	}
}
