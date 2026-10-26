import { describe, it, expect, vi } from 'vitest';
import { render, screen, within } from '@testing-library/svelte';
import BasketStoreTable from './BasketStoreTable.svelte';
import type { Basket, BasketStore, ShippingRate } from '$lib/api/client';

vi.mock('$app/paths', () => ({
	resolve: (path: string) => path
}));

function store(overrides: Partial<BasketStore> = {}): BasketStore {
	return {
		host: 'shop-a.es',
		currency: 'EUR',
		items: [
			{ productId: 'p1', price: 10, currency: 'EUR' },
			{ productId: 'p2', price: 5, currency: 'EUR' }
		],
		subtotal: 25,
		shipping: 3,
		total: 28,
		complete: true,
		missingCount: 0,
		...overrides
	};
}

function basket(stores: BasketStore[], bestStore: string | null = 'shop-a.es'): Basket {
	return {
		id: 'b1',
		name: 'Trip',
		description: null,
		createdAt: '2026-01-01T00:00:00Z',
		updatedAt: '2026-01-01T00:00:00Z',
		items: [
			{ productId: 'p1', productName: 'Tent', imageUrl: null, quantity: 2 },
			{ productId: 'p2', productName: 'Stove', imageUrl: null, quantity: 1 }
		],
		stores,
		bestStore,
		split: null
	};
}

const rate = (host: string): ShippingRate => ({ host, amount: 3, currency: 'EUR', freeAbove: 50 });

function row(host: string): HTMLElement {
	return screen
		.getAllByTestId('basket-store-row')
		.find((r) => r.getAttribute('data-host') === host)!;
}

describe('BasketStoreTable', () => {
	it('should render one row per store in the API order when stores are given', () => {
		render(BasketStoreTable, {
			props: {
				basket: basket([store(), store({ host: 'shop-b.es', total: 30 })]),
				shippingRates: [rate('shop-a.es')]
			}
		});

		const hosts = screen.getAllByTestId('basket-store-row').map((r) => r.getAttribute('data-host'));
		expect(hosts).toEqual(['shop-a.es', 'shop-b.es']);
	});

	it('should show one column header per item when rendered', () => {
		render(BasketStoreTable, { props: { basket: basket([store()]), shippingRates: [] } });

		const headers = screen.getAllByRole('columnheader').map((h) => h.textContent?.trim());
		expect(headers).toEqual(['Store', 'Tent', 'Stove', 'Shipping', 'Total']);
	});

	it('should show the line amount as unit price times quantity when an item has a price', () => {
		render(BasketStoreTable, { props: { basket: basket([store()]), shippingRates: [] } });

		// Tent: 10 x 2. Stove: 5 x 1.
		expect(within(row('shop-a.es')).getByText('€20.00')).toBeInTheDocument();
		expect(within(row('shop-a.es')).getByText('€5.00')).toBeInTheDocument();
	});

	it('should mark the best store with a Best badge when it is complete', () => {
		render(BasketStoreTable, {
			props: {
				basket: basket([store(), store({ host: 'shop-b.es', total: 30 })]),
				shippingRates: []
			}
		});

		expect(within(row('shop-a.es')).getByTestId('best-badge')).toHaveTextContent('Best');
		expect(within(row('shop-b.es')).queryByTestId('best-badge')).not.toBeInTheDocument();
	});

	it('should show "n of m items" and no badge when a store is incomplete', () => {
		const incomplete = store({
			host: 'shop-c.es',
			items: [
				{ productId: 'p1', price: 10, currency: 'EUR' },
				{ productId: 'p2', price: null, currency: null }
			],
			complete: false,
			missingCount: 1,
			total: null,
			subtotal: 20
		});
		render(BasketStoreTable, {
			props: { basket: basket([store(), incomplete], 'shop-c.es'), shippingRates: [] }
		});

		expect(within(row('shop-c.es')).getByText('1 of 2 items')).toBeInTheDocument();
		expect(within(row('shop-c.es')).queryByTestId('best-badge')).not.toBeInTheDocument();
	});

	it('should show "not available" when an item has no price at the store', () => {
		const incomplete = store({
			items: [
				{ productId: 'p1', price: 10, currency: 'EUR' },
				{ productId: 'p2', price: null, currency: null }
			],
			complete: false,
			missingCount: 1
		});
		render(BasketStoreTable, { props: { basket: basket([incomplete], null), shippingRates: [] } });

		expect(within(row('shop-a.es')).getByText('not available')).toBeInTheDocument();
	});

	it('should show a dash with a title when the host has no shipping rate and shipping is 0', () => {
		render(BasketStoreTable, {
			props: { basket: basket([store({ shipping: 0, total: 25 })]), shippingRates: [] }
		});

		const cell = within(row('shop-a.es')).getByTestId('shipping-cell');
		expect(cell).toHaveTextContent('—');
		expect(within(cell).getByTitle('No shipping cost set')).toBeInTheDocument();
	});

	it('should show "free" when the host has a rate and shipping is 0', () => {
		render(BasketStoreTable, {
			props: {
				basket: basket([store({ shipping: 0, total: 25 })]),
				shippingRates: [rate('shop-a.es')]
			}
		});

		expect(within(row('shop-a.es')).getByTestId('shipping-cell')).toHaveTextContent('free');
	});

	it('should show the shipping amount when shipping is above 0', () => {
		render(BasketStoreTable, {
			props: { basket: basket([store()]), shippingRates: [rate('shop-a.es')] }
		});

		expect(within(row('shop-a.es')).getByTestId('shipping-cell')).toHaveTextContent('€3.00');
	});

	it('should show "other currency" when shipping is null', () => {
		render(BasketStoreTable, {
			props: {
				basket: basket([store({ shipping: null, total: null })]),
				shippingRates: [rate('shop-a.es')]
			}
		});

		expect(within(row('shop-a.es')).getByTestId('shipping-cell')).toHaveTextContent(
			'other currency'
		);
	});

	it('should show the total when it is known', () => {
		render(BasketStoreTable, { props: { basket: basket([store()]), shippingRates: [] } });

		expect(within(row('shop-a.es')).getByTestId('total-cell')).toHaveTextContent('€28.00');
	});

	it('should explain an unknown total when the store mixes currencies', () => {
		render(BasketStoreTable, {
			props: {
				basket: basket([store({ currency: null, total: null, subtotal: null })]),
				shippingRates: []
			}
		});

		const cell = within(row('shop-a.es')).getByTestId('total-cell');
		expect(cell).toHaveTextContent('unknown');
		expect(within(cell).getByTitle('Prices in different currencies')).toBeInTheDocument();
	});

	it('should explain an unknown total when shipping is in another currency', () => {
		render(BasketStoreTable, {
			props: {
				basket: basket([store({ shipping: null, total: null })]),
				shippingRates: []
			}
		});

		const cell = within(row('shop-a.es')).getByTestId('total-cell');
		expect(within(cell).getByTitle('Shipping in another currency')).toBeInTheDocument();
	});

	it('should scroll inside its own container when the table is wide', () => {
		render(BasketStoreTable, { props: { basket: basket([store()]), shippingRates: [] } });

		expect(screen.getByRole('table').parentElement).toHaveClass('overflow-x-auto');
	});
});
