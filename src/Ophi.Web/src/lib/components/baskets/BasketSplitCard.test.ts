import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import BasketSplitCard from './BasketSplitCard.svelte';
import type { Basket, BasketSplit, BasketStore, ShippingRate } from '$lib/api/client';

vi.mock('$app/paths', () => ({
	resolve: (path: string) => path
}));

const bestStore: BasketStore = {
	host: 'shop-a.es',
	currency: 'EUR',
	items: [
		{ productId: 'p1', price: 10, currency: 'EUR' },
		{ productId: 'p2', price: 5, currency: 'EUR' }
	],
	subtotal: 25,
	shipping: 5,
	total: 30,
	complete: true,
	missingCount: 0
};

function split(overrides: Partial<BasketSplit> = {}): BasketSplit {
	return {
		currency: 'EUR',
		lines: [
			{ productId: 'p1', host: 'shop-a.es', price: 10 },
			{ productId: 'p2', host: 'shop-b.es', price: 3 }
		],
		shipping: [
			{ host: 'shop-a.es', shipping: 5 },
			{ host: 'shop-b.es', shipping: 0 }
		],
		total: 28,
		complete: true,
		...overrides
	};
}

function basket(s: BasketSplit | null, stores: BasketStore[] = [bestStore]): Basket {
	return {
		id: 'b1',
		name: 'Trip',
		description: null,
		createdAt: '2026-01-01T00:00:00Z',
		updatedAt: '2026-01-01T00:00:00Z',
		items: [
			{ productId: 'p1', productName: 'Tent', imageUrl: null, quantity: 1 },
			{ productId: 'p2', productName: 'Stove', imageUrl: null, quantity: 2 }
		],
		stores,
		bestStore: stores.length > 0 ? stores[0].host : null,
		split: s
	};
}

const rates: ShippingRate[] = [{ host: 'shop-b.es', amount: 4, currency: 'EUR', freeAbove: 20 }];

describe('BasketSplitCard', () => {
	it('should render nothing when the basket has no split', () => {
		render(BasketSplitCard, { props: { basket: basket(null), shippingRates: [] } });

		expect(screen.queryByTestId('basket-split')).not.toBeInTheDocument();
	});

	it('should render nothing when the split has no lines', () => {
		render(BasketSplitCard, {
			props: { basket: basket(split({ lines: [], shipping: [] })), shippingRates: [] }
		});

		expect(screen.queryByTestId('basket-split')).not.toBeInTheDocument();
	});

	it('should list each item with its host and line amount when a split exists', () => {
		render(BasketSplitCard, { props: { basket: basket(split()), shippingRates: rates } });

		const lines = screen.getAllByTestId('split-line');
		expect(lines[0]).toHaveTextContent('Tent');
		expect(lines[0]).toHaveTextContent('shop-a.es');
		expect(lines[0]).toHaveTextContent('€10.00');
		expect(lines[1]).toHaveTextContent('Stove');
		expect(lines[1]).toHaveTextContent('shop-b.es');
		// 3 x quantity 2
		expect(lines[1]).toHaveTextContent('€6.00');
	});

	it('should say the item is not available when the line has no host', () => {
		render(BasketSplitCard, {
			props: {
				basket: basket(
					split({
						lines: [
							{ productId: 'p1', host: 'shop-a.es', price: 10 },
							{ productId: 'p2', host: null, price: null }
						],
						shipping: [{ host: 'shop-a.es', shipping: 5 }],
						complete: false
					})
				),
				shippingRates: []
			}
		});

		expect(screen.getAllByTestId('split-line')[1]).toHaveTextContent('not available in EUR');
	});

	it('should show shipping per host with the same wording as the table', () => {
		render(BasketSplitCard, { props: { basket: basket(split()), shippingRates: rates } });

		const shipping = screen.getAllByTestId('split-shipping');
		expect(shipping[0]).toHaveTextContent('shop-a.es');
		expect(shipping[0]).toHaveTextContent('€5.00');
		expect(shipping[1]).toHaveTextContent('shop-b.es');
		expect(shipping[1]).toHaveTextContent('free');
	});

	it('should show the split total when it is known', () => {
		render(BasketSplitCard, { props: { basket: basket(split()), shippingRates: rates } });

		expect(screen.getByTestId('split-total')).toHaveTextContent('€28.00');
	});

	it('should say how much cheaper the split is when it costs less than the best store', () => {
		render(BasketSplitCard, { props: { basket: basket(split()), shippingRates: rates } });

		expect(screen.getByTestId('split-difference')).toHaveTextContent(
			'€2.00 cheaper than buying everything at shop-a.es'
		);
	});

	it('should say how much more the split costs when it costs more than the best store', () => {
		render(BasketSplitCard, {
			props: { basket: basket(split({ total: 33 })), shippingRates: rates }
		});

		expect(screen.getByTestId('split-difference')).toHaveTextContent(
			'€3.00 more than buying everything at shop-a.es (extra shipping)'
		);
	});

	it('should show no difference when the split is incomplete', () => {
		render(BasketSplitCard, {
			props: {
				basket: basket(
					split({
						lines: [
							{ productId: 'p1', host: 'shop-a.es', price: 10 },
							{ productId: 'p2', host: null, price: null }
						],
						complete: false
					})
				),
				shippingRates: rates
			}
		});

		expect(screen.queryByTestId('split-difference')).not.toBeInTheDocument();
		expect(screen.getByTestId('split-incomplete')).toBeInTheDocument();
	});

	it('should show no difference when the currencies differ', () => {
		render(BasketSplitCard, {
			props: { basket: basket(split({ currency: 'USD' })), shippingRates: rates }
		});

		expect(screen.queryByTestId('split-difference')).not.toBeInTheDocument();
	});

	it('should show no difference when there is no best store', () => {
		render(BasketSplitCard, {
			props: { basket: basket(split(), []), shippingRates: rates }
		});

		expect(screen.queryByTestId('split-difference')).not.toBeInTheDocument();
	});
});
