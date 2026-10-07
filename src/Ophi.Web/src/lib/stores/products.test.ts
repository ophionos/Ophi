import { describe, it, expect, beforeEach } from 'vitest';
import { products } from './products.svelte';
import type { Product } from '$lib/api/client';

function mockProduct(overrides: Partial<Product> = {}): Product {
	return {
		id: overrides.id ?? crypto.randomUUID(),
		name: overrides.name ?? 'Test Product',
		url: 'https://example.com',
		imageUrl: null,
		currentPrice: 100,
		previousPrice: null,
		priceChange: null,
		currency: 'USD',
		lastCheckedAt: null,
		status: 'active',
		isFavourite: false,
		isOutOfStock: false,
		urlCount: 1,
		alertCount: 0,
		tags: [],
		customFields: [],
		sparkline: null,
		priceMin: null,
		priceMax: null,
		dealScore: null,
		affiliateUrl: null,
		checkIntervalMinutes: null,
		...overrides
	} as Product;
}

describe('products store', () => {
	beforeEach(() => {
		products.clear();
	});

	it('should start with empty items', () => {
		expect(products.items).toEqual([]);
		expect(products.total).toBe(0);
	});

	it('should set products', () => {
		const items = [mockProduct({ name: 'A' }), mockProduct({ name: 'B' })];
		products.set(items);

		expect(products.items).toHaveLength(2);
		expect(products.items[0].name).toBe('A');
	});

	it('should set page with pagination metadata', () => {
		const items = [mockProduct()];
		products.setPage(items, 50, 2, 24);

		expect(products.items).toHaveLength(1);
		expect(products.total).toBe(50);
		expect(products.page).toBe(2);
		expect(products.pageSize).toBe(24);
	});

	it('should add a product', () => {
		const existing = mockProduct({ name: 'Existing' });
		products.set([existing]);

		const newProduct = mockProduct({ name: 'New' });
		products.add(newProduct);

		expect(products.items).toHaveLength(2);
		expect(products.items[1].name).toBe('New');
	});

	it('should remove a product by id', () => {
		const p1 = mockProduct({ id: 'keep' });
		const p2 = mockProduct({ id: 'remove' });
		products.set([p1, p2]);

		products.remove('remove');

		expect(products.items).toHaveLength(1);
		expect(products.items[0].id).toBe('keep');
	});

	it('should update a product by id', () => {
		const p = mockProduct({ id: 'p1', name: 'Old Name' });
		products.set([p]);

		products.update('p1', { name: 'New Name' });

		expect(products.items[0].name).toBe('New Name');
		expect(products.items[0].id).toBe('p1');
	});

	it('should not modify other products when updating', () => {
		const p1 = mockProduct({ id: 'p1', name: 'One' });
		const p2 = mockProduct({ id: 'p2', name: 'Two' });
		products.set([p1, p2]);

		products.update('p1', { name: 'Updated' });

		expect(products.items[1].name).toBe('Two');
	});

	it('should clear all products', () => {
		products.set([mockProduct(), mockProduct()]);

		products.clear();

		expect(products.items).toEqual([]);
	});
});
