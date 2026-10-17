import { describe, it, expect } from 'vitest';
import { bestPriceUrlId } from './bestPrice';
import type { ProductDetail, ProductUrl } from '$lib/api/client';

function makeUrl(overrides: Partial<ProductUrl> = {}): ProductUrl {
	return {
		id: 'u1',
		url: 'https://store.example/item',
		currentPrice: 100,
		currency: 'USD',
		failureCount: 0,
		status: 'active',
		isOutOfStock: false,
		...overrides
	};
}

function makeProduct(urls: ProductUrl[], currency = 'USD'): ProductDetail {
	return { id: 'p1', currency, urls } as ProductDetail;
}

describe('bestPriceUrlId', () => {
	it('should return the cheapest URL when several share the product currency', () => {
		const product = makeProduct([
			makeUrl({ id: 'a', currentPrice: 120 }),
			makeUrl({ id: 'b', currentPrice: 99 })
		]);
		expect(bestPriceUrlId(product)).toBe('b');
	});

	it('should ignore a numerically lower price in another currency', () => {
		const product = makeProduct([
			makeUrl({ id: 'usd', currentPrice: 100, currency: 'USD' }),
			makeUrl({ id: 'usd-dear', currentPrice: 110, currency: 'USD' }),
			makeUrl({ id: 'eur', currentPrice: 95, currency: 'EUR' })
		]);
		expect(bestPriceUrlId(product)).toBe('usd');
	});

	it('should ignore a paused URL whose frozen price is lower', () => {
		const product = makeProduct([
			makeUrl({ id: 'live', currentPrice: 100 }),
			makeUrl({ id: 'live-dear', currentPrice: 110 }),
			makeUrl({ id: 'paused', currentPrice: 50, status: 'paused' })
		]);
		expect(bestPriceUrlId(product)).toBe('live');
	});

	it('should match the product currency case-insensitively', () => {
		const product = makeProduct(
			[
				makeUrl({ id: 'a', currentPrice: 100, currency: 'usd' }),
				makeUrl({ id: 'b', currentPrice: 90 })
			],
			'USD'
		);
		expect(bestPriceUrlId(product)).toBe('b');
	});

	it('should return null when only one URL has a price', () => {
		const product = makeProduct([
			makeUrl({ id: 'a', currentPrice: 100 }),
			makeUrl({ id: 'b', currentPrice: undefined })
		]);
		expect(bestPriceUrlId(product)).toBeNull();
	});

	it('should return null when the only other priced URL is paused or in another currency', () => {
		const product = makeProduct([
			makeUrl({ id: 'a', currentPrice: 100 }),
			makeUrl({ id: 'b', currentPrice: 50, status: 'paused' }),
			makeUrl({ id: 'c', currentPrice: 40, currency: 'EUR' })
		]);
		expect(bestPriceUrlId(product)).toBeNull();
	});

	it('should return null for a missing product', () => {
		expect(bestPriceUrlId(null)).toBeNull();
	});
});
