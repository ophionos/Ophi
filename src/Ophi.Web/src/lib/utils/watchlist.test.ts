import { describe, it, expect } from 'vitest';
import type { AlertTarget, Product } from '$lib/api/client';
import { formatPrice } from '$lib/format';
import {
	attentionLabel,
	storeLabel,
	isPriceOld,
	alertTargetText,
	alertDistanceText,
	productInitial,
	fixAction,
	attentionCause,
	hostOf,
	trackedText,
	rangeTitle,
	storeLine,
	PENDING_TEXT,
	STALE_AFTER_HOURS
} from './watchlist';

const base: Product = {
	id: 'p1',
	name: 'Desk Lamp',
	url: 'https://www.shop.example/lamp',
	currency: 'USD',
	status: 'active',
	isFavourite: false,
	isOutOfStock: false,
	storeCount: 1,
	alertCount: 0,
	tags: [],
	customFields: []
};

const target = (overrides: Partial<AlertTarget>): AlertTarget => ({
	alertId: 'a1',
	condition: 'below',
	targetPrice: 100,
	gap: 4,
	isNear: true,
	...overrides
});

describe('attentionLabel', () => {
	it('should name every attention kind in plain words', () => {
		expect(attentionLabel('blocked')).toBe('Blocked');
		expect(attentionLabel('priceNotFound')).toBe('Price not found');
		expect(attentionLabel('failing')).toBe('Failing');
		expect(attentionLabel('priceAnomaly')).toBe('Check price');
		expect(attentionLabel('outOfStock')).toBe('Out of stock');
		expect(attentionLabel('paused')).toBe('Paused by you');
	});
});

describe('storeLabel', () => {
	it('should use the configured store name when there is one', () => {
		expect(storeLabel({ ...base, bestStoreName: 'The Shop' })).toBe('The Shop');
	});

	it('should fall back to the host of the best URL without www', () => {
		expect(storeLabel({ ...base, bestUrl: 'https://www.cheaper.example/x' })).toBe('cheaper.example');
		expect(storeLabel(base)).toBe('shop.example');
	});

	it('should return an empty label when the URL cannot be parsed', () => {
		expect(storeLabel({ ...base, url: '' })).toBe('');
	});
});

describe('isPriceOld', () => {
	const now = new Date('2026-10-18T12:00:00Z');

	it('should be true when the last success is older than the stale limit', () => {
		const at = new Date(now.getTime() - (STALE_AFTER_HOURS + 1) * 3_600_000).toISOString();
		expect(isPriceOld({ ...base, currentPrice: 10, lastSucceededAt: at }, now)).toBe(true);
	});

	it('should be false for a recent success, no price, or no recorded success', () => {
		const recent = new Date(now.getTime() - 3_600_000).toISOString();
		expect(isPriceOld({ ...base, currentPrice: 10, lastSucceededAt: recent }, now)).toBe(false);
		expect(isPriceOld({ ...base, lastSucceededAt: '2020-01-01T00:00:00Z' }, now)).toBe(false);
		expect(isPriceOld({ ...base, currentPrice: 10 }, now)).toBe(false);
	});
});

describe('alertTargetText', () => {
	it('should show the direction of the target', () => {
		expect(alertTargetText(target({}), 'USD')).toBe('≤ $100.00');
		expect(alertTargetText(target({ condition: 'above' }), 'USD')).toBe('≥ $100.00');
	});
});

describe('alertDistanceText', () => {
	it('should say how far the price still has to move', () => {
		expect(alertDistanceText(target({ gap: 4 }), 'USD')).toBe('$4.00 away');
	});

	it('should say reached instead of a negative distance', () => {
		expect(alertDistanceText(target({ gap: 0 }), 'USD')).toBe('Reached');
		expect(alertDistanceText(target({ gap: -5.01 }), 'USD')).toBe('Reached');
	});
});

describe('productInitial', () => {
	it('should use the first letter or digit of the name', () => {
		expect(productInitial('desk lamp')).toBe('D');
		expect(productInitial('  "4K" monitor')).toBe('4');
		expect(productInitial('')).toBe('?');
	});
});

describe('fixAction', () => {
	it('should offer the check only when challenges are available', () => {
		expect(fixAction('blocked', true)).toEqual({ kind: 'challenge', label: 'Solve check' });
		expect(fixAction('blocked', false)).toEqual({ kind: 'retry', label: 'Retry now' });
	});

	it('should retry a failing URL and send the rest to the product page', () => {
		expect(fixAction('failing', true).kind).toBe('retry');
		expect(fixAction('priceNotFound', true)).toEqual({ kind: 'link', label: 'Open product' });
		expect(fixAction('priceAnomaly', true)).toEqual({ kind: 'link', label: 'Review' });
	});
});

describe('attentionCause', () => {
	it('should state the cause with the failure count for a failing URL', () => {
		const attention = { kind: 'failing' as const, isAction: true, productUrlId: 'u', url: 'https://x.test', consecutiveFailures: 4 };
		expect(attentionCause(attention)).toBe('4 failed checks in a row');
		expect(attentionCause({ ...attention, kind: 'blocked' })).toBe('The store blocks automated checks');
	});
});

describe('hostOf', () => {
	it('should drop www. and survive a bad URL', () => {
		expect(hostOf('https://www.shop.example/p/1')).toBe('shop.example');
		expect(hostOf('not a url')).toBe('');
	});
});

describe('trackedText', () => {
	const now = new Date('2026-10-26T12:00:00Z');
	const ago = (hours: number) => new Date(now.getTime() - hours * 3_600_000).toISOString();

	it('should return null when the tracking start is missing', () => {
		expect(trackedText(base, now)).toBeNull();
		expect(trackedText({ ...base, trackedSince: null }, now)).toBeNull();
	});

	it('should say tracked today within the first day', () => {
		expect(trackedText({ ...base, trackedSince: ago(5) }, now)).toBe('tracked today');
	});

	it('should say tracked 1 day after one whole day', () => {
		expect(trackedText({ ...base, trackedSince: ago(30) }, now)).toBe('tracked 1 day');
	});

	it('should count whole days with a plural', () => {
		expect(trackedText({ ...base, trackedSince: ago(24 * 4 + 20) }, now)).toBe('tracked 4 days');
	});
});

describe('rangeTitle', () => {
	it('should word the 90-day range with an en dash', () => {
		expect(rangeTitle({ ...base, currency: 'EUR', priceMin: 15.8, priceMax: 18.83 })).toBe(
			`90 days: ${formatPrice(15.8, 'EUR')}–${formatPrice(18.83, 'EUR')}`
		);
	});

	it('should return undefined when the range is missing', () => {
		expect(rangeTitle(base)).toBeUndefined();
		expect(rangeTitle({ ...base, priceMin: 1 })).toBeUndefined();
	});
});

describe('storeLine', () => {
	it('should be the store alone for a single-store product', () => {
		expect(storeLine({ ...base, bestStoreName: 'The Shop' })).toBe('The Shop');
	});

	it('should count the other stores', () => {
		expect(storeLine({ ...base, bestStoreName: 'The Shop', storeCount: 3 })).toBe('The Shop · 2 more');
	});

	it('should end with the new cheapest store note when the flag is set', () => {
		expect(storeLine({ ...base, bestStoreName: 'The Shop', storeCount: 2, newCheapestStore: true })).toBe(
			'The Shop · 1 more · new cheapest store'
		);
	});
});

describe('PENDING_TEXT', () => {
	it('should be one wording with the ellipsis character', () => {
		expect(PENDING_TEXT).toBe('Fetching price…');
	});
});
