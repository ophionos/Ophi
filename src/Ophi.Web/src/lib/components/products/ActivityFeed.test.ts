import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import ActivityFeed from './ActivityFeed.svelte';
import type { Product } from '$lib/api/client';

function makeProduct(overrides: Partial<Product> = {}): Product {
	return {
		id: crypto.randomUUID(),
		name: 'Test Product',
		url: 'https://example.com',
		currency: 'USD',
		status: 'active',
		isFavourite: false,
		isOutOfStock: false,
		storeCount: 1,
		alertCount: 0,
		tags: [],
		customFields: [],
		currentPrice: 100,
		previousPrice: 120,
		priceChange: -16.67,
		lastChecked: new Date().toISOString(),
		...overrides
	};
}

describe('ActivityFeed', () => {
	it('should render price drop events', () => {
		const products = [makeProduct({ name: 'Sale Item', priceChange: -10 })];
		render(ActivityFeed, { props: { products } });
		expect(screen.getByText('Sale Item')).toBeTruthy();
		expect(screen.getByText(/Price dropped/)).toBeTruthy();
	});

	it('should render price increase events', () => {
		const products = [
			makeProduct({
				name: 'Rising Product',
				currentPrice: 130,
				previousPrice: 100,
				priceChange: 30
			})
		];
		render(ActivityFeed, { props: { products } });
		expect(screen.getByText('Rising Product')).toBeTruthy();
		expect(screen.getByText(/increased/i)).toBeTruthy();
	});

	it('should show empty state when no events', () => {
		render(ActivityFeed, { props: { products: [] } });
		expect(screen.getByText(/no recent activity/i)).toBeTruthy();
	});

	it('should show empty state when no price changes', () => {
		const products = [makeProduct({ priceChange: undefined, previousPrice: undefined })];
		render(ActivityFeed, { props: { products } });
		expect(screen.getByText(/no recent activity/i)).toBeTruthy();
	});

	it('should sort events by lastChecked descending', () => {
		const products = [
			makeProduct({
				name: 'Older',
				priceChange: -5,
				lastChecked: '2026-03-15T00:00:00Z'
			}),
			makeProduct({
				name: 'Newer',
				priceChange: -10,
				lastChecked: '2026-03-17T00:00:00Z'
			})
		];
		const { container } = render(ActivityFeed, { props: { products } });
		const names = container.querySelectorAll('[data-testid="event-product-name"]');
		expect(names[0]?.textContent).toBe('Newer');
		expect(names[1]?.textContent).toBe('Older');
	});

	it('should display price change amount', () => {
		const products = [
			makeProduct({
				name: 'Deal Product',
				currentPrice: 80,
				previousPrice: 100,
				priceChange: -20,
				currency: 'USD'
			})
		];
		render(ActivityFeed, { props: { products } });
		expect(screen.getByText(/20\.00/)).toBeTruthy();
	});

	it('should show relative time for events', () => {
		const products = [
			makeProduct({ priceChange: -5, lastChecked: new Date().toISOString() })
		];
		render(ActivityFeed, { props: { products } });
		// Should show some time reference (e.g., "just now" or "X seconds ago")
		const container = document.body;
		expect(container.textContent).toMatch(/ago|just now|seconds|minutes|hours/i);
	});

	it('should show deal score badge when available', () => {
		const products = [makeProduct({ priceChange: -10, dealScore: 85 })];
		const { container } = render(ActivityFeed, { props: { products } });
		expect(container.querySelector('[aria-label*="Deal score"]')).toBeTruthy();
	});

	it('should handle products with alerts', () => {
		const products = [makeProduct({ priceChange: -10, alertCount: 2 })];
		render(ActivityFeed, { props: { products } });
		expect(screen.getByText(/2 alerts/i)).toBeTruthy();
	});

	it('should show out of stock events', () => {
		const products = [makeProduct({ name: 'OOS Product', isOutOfStock: true })];
		render(ActivityFeed, { props: { products } });
		expect(screen.getByText('OOS Product')).toBeTruthy();
		expect(screen.getByText(/out of stock/i)).toBeTruthy();
	});

	it('should show last known price for OOS product', () => {
		const products = [
			makeProduct({ name: 'OOS With Price', isOutOfStock: true, currentPrice: 59.99, currency: 'USD' })
		];
		render(ActivityFeed, { props: { products } });
		expect(screen.getByText(/last known/)).toBeTruthy();
		expect(screen.getByText(/59\.99/)).toBeTruthy();
	});

	it('should show OOS label in price column', () => {
		const products = [
			makeProduct({ isOutOfStock: true, currentPrice: undefined })
		];
		render(ActivityFeed, { props: { products } });
		expect(screen.getByText('OOS')).toBeTruthy();
	});
});
