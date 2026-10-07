import { describe, it, expect } from 'vitest';
import { getPriceStory, type PriceStoryInput } from './priceStory';

describe('getPriceStory', () => {
	it('should return "Just dropped!" when price just decreased', () => {
		const input: PriceStoryInput = {
			currentPrice: 80,
			priceChange: -10,
			sparkline: [
				{ date: '2026-03-14', price: 100 },
				{ date: '2026-03-15', price: 95 },
				{ date: '2026-03-16', price: 90 },
				{ date: '2026-03-17', price: 80 }
			]
		};
		const result = getPriceStory(input);
		expect(result).not.toBeNull();
		expect(result!.text).toBe('Just dropped!');
		expect(result!.variant).toBe('positive');
	});

	it('should return "Lowest in 14 days" when at minimum with enough history', () => {
		const input: PriceStoryInput = {
			currentPrice: 50,
			priceMin: 50,
			priceMax: 100,
			sparkline: [
				{ date: '2026-03-10', price: 100 },
				{ date: '2026-03-11', price: 90 },
				{ date: '2026-03-12', price: 80 },
				{ date: '2026-03-13', price: 70 },
				{ date: '2026-03-14', price: 60 },
				{ date: '2026-03-15', price: 55 },
				{ date: '2026-03-16', price: 50 },
				// Note: last two points same price, no "just dropped"
				{ date: '2026-03-17', price: 50 }
			]
		};
		const result = getPriceStory(input);
		expect(result).not.toBeNull();
		expect(result!.text).toBe('Lowest in 14 days');
		expect(result!.variant).toBe('highlight');
	});

	it('should return "Near 14-day low" when within 5% of minimum', () => {
		const input: PriceStoryInput = {
			currentPrice: 52,
			priceMin: 50,
			priceMax: 100,
			// Last two points same = no "just dropped"
			sparkline: [
				{ date: '2026-03-15', price: 60 },
				{ date: '2026-03-16', price: 52 },
				{ date: '2026-03-17', price: 52 }
			]
		};
		const result = getPriceStory(input);
		expect(result).not.toBeNull();
		expect(result!.text).toBe('Near 14-day low');
		expect(result!.variant).toBe('positive');
	});

	it('should return "X% below average" when significantly below average', () => {
		const input: PriceStoryInput = {
			currentPrice: 70,
			priceMin: 60,
			priceMax: 120,
			sparkline: [
				{ date: '2026-03-13', price: 100 },
				{ date: '2026-03-14', price: 110 },
				{ date: '2026-03-15', price: 120 },
				{ date: '2026-03-16', price: 100 },
				// Price is 70, average is 100 → 30% below
				{ date: '2026-03-17', price: 70 }
			]
		};
		const result = getPriceStory(input);
		expect(result).not.toBeNull();
		expect(result!.text).toMatch(/\d+% below average/);
		expect(result!.variant).toBe('positive');
	});

	it('should return "Steady for X days" when price unchanged', () => {
		const input: PriceStoryInput = {
			currentPrice: 100,
			priceMin: 100,
			priceMax: 100,
			priceChange: 0,
			sparkline: [
				{ date: '2026-03-12', price: 100 },
				{ date: '2026-03-13', price: 100 },
				{ date: '2026-03-14', price: 100 },
				{ date: '2026-03-15', price: 100 },
				{ date: '2026-03-16', price: 100 },
				{ date: '2026-03-17', price: 100 }
			]
		};
		const result = getPriceStory(input);
		expect(result).not.toBeNull();
		expect(result!.text).toMatch(/Steady for \d+ days/);
		expect(result!.variant).toBe('neutral');
	});

	it('should return "Trending down" as fallback for negative priceChange', () => {
		// Sparkline shows flat last two points (no "just dropped"),
		// but priceChange still negative from earlier comparison
		const input: PriceStoryInput = {
			currentPrice: 90,
			priceChange: -3,
			priceMin: 85,
			priceMax: 100,
			sparkline: [
				{ date: '2026-03-16', price: 90 },
				{ date: '2026-03-17', price: 90 }
			]
		};
		const result = getPriceStory(input);
		expect(result).not.toBeNull();
		expect(result!.text).toBe('Trending down');
		expect(result!.variant).toBe('positive');
	});

	it('should return "Price rising" for significant positive priceChange', () => {
		const input: PriceStoryInput = {
			currentPrice: 150,
			priceChange: 15,
			priceMin: 100,
			priceMax: 150,
			sparkline: [
				{ date: '2026-03-16', price: 148 },
				{ date: '2026-03-17', price: 150 }
			]
		};
		const result = getPriceStory(input);
		expect(result).not.toBeNull();
		expect(result!.text).toBe('Price rising');
		expect(result!.variant).toBe('negative');
	});

	it('should return null when no data available', () => {
		const result = getPriceStory({});
		expect(result).toBeNull();
	});

	it('should return null when only one sparkline point', () => {
		const input: PriceStoryInput = {
			currentPrice: 100,
			sparkline: [{ date: '2026-03-17', price: 100 }]
		};
		const result = getPriceStory(input);
		expect(result).toBeNull();
	});

	it('should prioritize "Just dropped!" over "Lowest in 14 days"', () => {
		const input: PriceStoryInput = {
			currentPrice: 50,
			priceMin: 50,
			priceMax: 100,
			priceChange: -10,
			sparkline: [
				{ date: '2026-03-10', price: 100 },
				{ date: '2026-03-11', price: 90 },
				{ date: '2026-03-12', price: 80 },
				{ date: '2026-03-13', price: 70 },
				{ date: '2026-03-14', price: 65 },
				{ date: '2026-03-15', price: 60 },
				{ date: '2026-03-16', price: 55 },
				{ date: '2026-03-17', price: 50 }
			]
		};
		const result = getPriceStory(input);
		expect(result).not.toBeNull();
		expect(result!.text).toBe('Just dropped!');
	});

	it('should handle missing sparkline gracefully', () => {
		const input: PriceStoryInput = {
			currentPrice: 100,
			priceChange: -5
		};
		const result = getPriceStory(input);
		// Should still work with priceChange alone
		expect(result).not.toBeNull();
		expect(result!.text).toBe('Trending down');
	});
});
