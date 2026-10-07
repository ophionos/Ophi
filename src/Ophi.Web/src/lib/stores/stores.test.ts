import { describe, it, expect, beforeEach } from 'vitest';
import { stores } from './stores.svelte';
import type { Store } from '$lib/api/client';

function mockStore(overrides: Partial<Store> = {}): Store {
	return {
		storeId: overrides.storeId ?? 'store-' + crypto.randomUUID(),
		name: overrides.name ?? 'Test Store',
		domainPatterns: ['example.com'],
		selectors: {
			priceSelectors: [],
			nameSelectors: [],
			imageSelectors: []
		},
		requiresJavaScript: false,
		isBuiltIn: false,
		priceLocale: 'en-US',
		...overrides
	} as Store;
}

describe('stores store', () => {
	beforeEach(() => {
		stores.clear();
	});

	it('should start with empty items', () => {
		expect(stores.items).toEqual([]);
	});

	it('should set stores', () => {
		stores.set([mockStore({ name: 'A' }), mockStore({ name: 'B' })]);
		expect(stores.items).toHaveLength(2);
	});

	it('should add a store', () => {
		stores.set([mockStore({ name: 'Existing' })]);
		stores.add(mockStore({ name: 'New' }));

		expect(stores.items).toHaveLength(2);
		expect(stores.items[1].name).toBe('New');
	});

	it('should update a store by storeId', () => {
		const s = mockStore({ storeId: 's1', name: 'Old' });
		stores.set([s]);

		stores.update('s1', mockStore({ storeId: 's1', name: 'Updated' }));

		expect(stores.items[0].name).toBe('Updated');
	});

	it('should remove a store by storeId', () => {
		const s1 = mockStore({ storeId: 'keep' });
		const s2 = mockStore({ storeId: 'remove' });
		stores.set([s1, s2]);

		stores.remove('remove');

		expect(stores.items).toHaveLength(1);
		expect(stores.items[0].storeId).toBe('keep');
	});

	it('should clear all stores', () => {
		stores.set([mockStore(), mockStore()]);
		stores.clear();
		expect(stores.items).toEqual([]);
	});
});
