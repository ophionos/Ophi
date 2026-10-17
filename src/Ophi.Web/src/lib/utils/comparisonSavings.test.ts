import { describe, it, expect } from 'vitest';
import { savingsVsNext } from './comparisonSavings';
import type { ComparisonGroupDetail, ComparisonProduct } from '$lib/api/client';

function makeProduct(overrides: Partial<ComparisonProduct> = {}): ComparisonProduct {
	return {
		id: 'p1',
		name: 'Product',
		url: 'https://store.example/item',
		currentPrice: 100,
		currency: 'USD',
		isBestPrice: false,
		priceHistory: [],
		customFields: [],
		...overrides
	};
}

function makeGroup(
	products: ComparisonProduct[],
	bestPriceProductId?: string
): ComparisonGroupDetail {
	const best = products.find((p) => p.id === bestPriceProductId);
	return {
		id: 'g1',
		name: 'Group',
		products,
		bestPriceProductId,
		bestPrice: best?.currentPrice,
		updatedAt: '2026-10-17T00:00:00Z'
	};
}

describe('savingsVsNext', () => {
	it('should return the gap to the next cheapest product in the same currency', () => {
		const group = makeGroup(
			[
				makeProduct({ id: 'best', currentPrice: 80 }),
				makeProduct({ id: 'next', currentPrice: 95 }),
				makeProduct({ id: 'dear', currentPrice: 120 })
			],
			'best'
		);
		expect(savingsVsNext(group)).toBe(15);
	});

	it('should ignore products in another currency', () => {
		const group = makeGroup(
			[
				makeProduct({ id: 'best', currentPrice: 80, currency: 'USD' }),
				makeProduct({ id: 'eur', currentPrice: 82, currency: 'EUR' }),
				makeProduct({ id: 'next', currentPrice: 95, currency: 'USD' })
			],
			'best'
		);
		expect(savingsVsNext(group)).toBe(15);
	});

	it('should start from the best product, not the lowest raw number', () => {
		// The backend picks the best within the dominant currency, so a smaller number in a
		// minority currency can sort first.
		const group = makeGroup(
			[
				makeProduct({ id: 'gbp', currentPrice: 20, currency: 'GBP' }),
				makeProduct({ id: 'best', currentPrice: 30, currency: 'EUR' }),
				makeProduct({ id: 'next', currentPrice: 34, currency: 'EUR' })
			],
			'best'
		);
		expect(savingsVsNext(group)).toBe(4);
	});

	it('should match currencies case-insensitively', () => {
		const group = makeGroup(
			[
				makeProduct({ id: 'best', currentPrice: 80, currency: 'USD' }),
				makeProduct({ id: 'next', currentPrice: 90, currency: 'usd' })
			],
			'best'
		);
		expect(savingsVsNext(group)).toBe(10);
	});

	it('should return null when no other product shares the best currency', () => {
		const group = makeGroup(
			[
				makeProduct({ id: 'best', currentPrice: 80, currency: 'USD' }),
				makeProduct({ id: 'eur', currentPrice: 90, currency: 'EUR' }),
				makeProduct({ id: 'unpriced', currentPrice: undefined, currency: 'USD' })
			],
			'best'
		);
		expect(savingsVsNext(group)).toBeNull();
	});

	it('should return null when the group has no best product', () => {
		const group = makeGroup([makeProduct({ id: 'a' }), makeProduct({ id: 'b' })]);
		expect(savingsVsNext(group)).toBeNull();
		expect(savingsVsNext(null)).toBeNull();
	});
});
