import { describe, it, expect } from 'vitest';
import { isAwaitingScrape } from './awaitingScrape';
import type { ProductDetail } from '$lib/api/client';

function makeProduct(overrides: Partial<ProductDetail> = {}): ProductDetail {
	return {
		id: 'p1',
		name: 'Test',
		currency: 'EUR',
		status: 'active',
		isFavourite: false,
		isOutOfStock: false,
		storeCount: 1,
		alertCount: 0,
		tags: [],
		customFields: [],
		urls: [],
		hasCurrencyMismatch: false,
		statistics: { min: 0, max: 0, average: 0 },
		alerts: [],
		...overrides
	} as ProductDetail;
}

function makeUrl(overrides: Partial<ProductDetail['urls'][number]> = {}) {
	return {
		id: 'u1',
		url: 'https://example.com/p',
		currency: 'EUR',
		failureCount: 0,
		status: 'active',
		isOutOfStock: false,
		...overrides
	} as ProductDetail['urls'][number];
}

describe('isAwaitingScrape', () => {
	it('should return false when product is null', () => {
		expect(isAwaitingScrape(null)).toBe(false);
	});

	it('should return true when the product status is pending', () => {
		expect(isAwaitingScrape(makeProduct({ status: 'pending' }))).toBe(true);
	});

	it('should return true when a URL has no price and was never checked', () => {
		const product = makeProduct({
			urls: [makeUrl({ currentPrice: undefined, lastCheckedAt: undefined, status: 'active' })]
		});
		expect(isAwaitingScrape(product)).toBe(true);
	});

	it('should return false when every URL already has a price', () => {
		const product = makeProduct({
			urls: [makeUrl({ currentPrice: 279.99, lastCheckedAt: '2026-06-01T00:00:00Z' })]
		});
		expect(isAwaitingScrape(product)).toBe(false);
	});

	it('should return false when an unpriced URL has already been checked (bounding)', () => {
		const product = makeProduct({
			urls: [makeUrl({ currentPrice: undefined, lastCheckedAt: '2026-06-01T00:00:00Z' })]
		});
		expect(isAwaitingScrape(product)).toBe(false);
	});

	it('should return false when an unpriced URL is in error or paused', () => {
		expect(
			isAwaitingScrape(makeProduct({ urls: [makeUrl({ currentPrice: undefined, status: 'error' })] }))
		).toBe(false);
		expect(
			isAwaitingScrape(makeProduct({ urls: [makeUrl({ currentPrice: undefined, status: 'paused' })] }))
		).toBe(false);
	});

	it('should return true when one of several URLs is still awaiting its first scrape', () => {
		const product = makeProduct({
			urls: [
				makeUrl({ id: 'u1', currentPrice: 279.99, lastCheckedAt: '2026-06-01T00:00:00Z' }),
				makeUrl({ id: 'u2', currentPrice: undefined, lastCheckedAt: undefined, status: 'active' })
			]
		});
		expect(isAwaitingScrape(product)).toBe(true);
	});
});
