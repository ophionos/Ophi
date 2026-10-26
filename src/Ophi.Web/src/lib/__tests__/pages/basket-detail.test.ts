import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/svelte';
import BasketDetailPage from '../../../routes/baskets/[id]/+page.svelte';
import { api, ApiError, type Basket, type BasketHistory } from '$lib/api/client';
import { goto } from '$app/navigation';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: {
			getBasket: vi.fn(),
			updateBasket: vi.fn(),
			deleteBasket: vi.fn(),
			setBasketItemQuantity: vi.fn(),
			removeBasketItem: vi.fn(),
			getBasketHistory: vi.fn()
		}
	};
});

vi.mock('$app/navigation', () => ({ goto: vi.fn() }));

function basket(overrides: Partial<Basket> = {}): Basket {
	return {
		id: 'b1',
		name: 'Camping',
		description: 'Weekend gear',
		createdAt: '2026-01-01T00:00:00Z',
		updatedAt: '2026-01-01T00:00:00Z',
		items: [
			{ productId: 'p1', productName: 'Tent', imageUrl: null, quantity: 1 },
			{ productId: 'p2', productName: 'Stove', imageUrl: null, quantity: 1 }
		],
		stores: [
			{
				host: 'shop-a.es',
				currency: 'EUR',
				items: [
					{ productId: 'p1', price: 100, currency: 'EUR' },
					{ productId: 'p2', price: 50, currency: 'EUR' }
				],
				subtotal: 150,
				shipping: 5,
				total: 155,
				complete: true,
				missingCount: 0
			}
		],
		bestStore: 'shop-a.es',
		split: null,
		...overrides
	};
}

const history: BasketHistory = {
	days: 30,
	series: [
		{
			host: 'shop-a.es',
			currency: 'EUR',
			points: [
				{ date: '2026-10-01T00:00:00Z', total: 160 },
				{ date: '2026-10-10T00:00:00Z', total: 155 }
			]
		}
	]
};

function renderPage(b: Basket, h: BasketHistory | null = history) {
	return render(BasketDetailPage, {
		props: { data: { basket: b, shippingRates: [], history: h } } as never
	});
}

describe('Basket detail page', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		vi.mocked(api.getBasketHistory).mockResolvedValue(history);
	});

	it('should show the name, description and the store table when loaded', () => {
		renderPage(basket());

		expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Camping');
		expect(screen.getByText('Weekend gear')).toBeInTheDocument();
		expect(screen.getAllByTestId('basket-store-row')).toHaveLength(1);
	});

	it('should link each item to its product page when items are listed', () => {
		renderPage(basket());

		const list = screen.getByTestId('basket-items');
		expect(within(list).getByRole('link', { name: 'Tent' })).toHaveAttribute('href', '/products/p1');
	});

	it('should save the quantity and update the totals from the response when the quantity changes', async () => {
		const updated = basket({
			stores: [{ ...basket().stores[0], total: 255, subtotal: 250 }]
		});
		vi.mocked(api.setBasketItemQuantity).mockResolvedValue(updated);
		renderPage(basket());

		const input = screen.getByLabelText('Quantity of Tent');
		await fireEvent.input(input, { target: { value: '2' } });
		await fireEvent.change(input);

		await waitFor(() => expect(api.setBasketItemQuantity).toHaveBeenCalledWith('b1', 'p1', 2));
		await waitFor(() =>
			expect(within(screen.getByTestId('total-cell')).getByText('€255.00')).toBeInTheDocument()
		);
	});

	it('should show the server message and revert when the quantity is rejected', async () => {
		vi.mocked(api.setBasketItemQuantity).mockRejectedValue(
			// The API's 400: a generic title, the reason under the field.
			new ApiError('VALIDATION', 'One or more validation errors occurred', {
				Quantity: ['Quantity must be between 1 and 999']
			})
		);
		renderPage(basket());

		const input = screen.getByLabelText('Quantity of Tent') as HTMLInputElement;
		await fireEvent.input(input, { target: { value: '5000' } });
		await fireEvent.change(input);

		expect(await screen.findByText('Quantity must be between 1 and 999')).toBeInTheDocument();
		await waitFor(() => expect(input.value).toBe('1'));
	});

	it('should remove the item and reload the basket when remove is clicked', async () => {
		vi.mocked(api.removeBasketItem).mockResolvedValue(undefined as never);
		vi.mocked(api.getBasket).mockResolvedValue(
			basket({ items: [basket().items[1]], stores: [], bestStore: null })
		);
		renderPage(basket());

		await fireEvent.click(screen.getByRole('button', { name: 'Remove Tent' }));

		await waitFor(() => expect(api.removeBasketItem).toHaveBeenCalledWith('b1', 'p1'));
		await waitFor(() => expect(api.getBasket).toHaveBeenCalledWith('b1'));
		await waitFor(() => expect(screen.queryByText('Tent')).not.toBeInTheDocument());
	});

	it('should show the total over time when the basket has items', () => {
		renderPage(basket());

		expect(screen.getByRole('heading', { name: /Total over time/ })).toBeInTheDocument();
		expect(screen.getByText(/current shipping rate/)).toBeInTheDocument();
	});

	it('should say the history is unavailable when it failed to load', () => {
		renderPage(basket(), null);

		expect(screen.getByText(/history is not available/i)).toBeInTheDocument();
	});

	it('should reload the history when the quantity changes', async () => {
		vi.mocked(api.setBasketItemQuantity).mockResolvedValue(basket());
		vi.mocked(api.getBasketHistory).mockResolvedValue(history);
		renderPage(basket());

		const input = screen.getByLabelText('Quantity of Tent');
		await fireEvent.input(input, { target: { value: '2' } });
		await fireEvent.change(input);

		await waitFor(() => expect(api.getBasketHistory).toHaveBeenCalledWith('b1', 30));
	});

	it('should hint how to add products when the basket is empty', () => {
		renderPage(basket({ items: [], stores: [], bestStore: null }));

		expect(screen.getByText(/Add products from a product page/)).toBeInTheDocument();
		expect(screen.queryByTestId('basket-store-row')).not.toBeInTheDocument();
	});

	it('should delete the basket only after confirmation and go to the list', async () => {
		vi.mocked(api.deleteBasket).mockResolvedValue(undefined as never);
		renderPage(basket());

		await fireEvent.click(screen.getByRole('button', { name: /Delete basket/ }));
		expect(api.deleteBasket).not.toHaveBeenCalled();
		const dialog = await screen.findByRole('dialog');
		await fireEvent.click(within(dialog).getByRole('button', { name: /^Delete$/ }));

		await waitFor(() => expect(api.deleteBasket).toHaveBeenCalledWith('b1'));
		await waitFor(() => expect(goto).toHaveBeenCalledWith('/baskets'));
	});

	it('should rename the basket when the rename modal is saved', async () => {
		vi.mocked(api.updateBasket).mockResolvedValue(basket({ name: 'Trip' }));
		renderPage(basket());

		await fireEvent.click(screen.getByRole('button', { name: /Rename/ }));
		const dialog = await screen.findByRole('dialog', { name: 'Rename basket' });
		await fireEvent.input(within(dialog).getByLabelText(/Name/), { target: { value: 'Trip' } });
		await fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));

		await waitFor(() => expect(api.updateBasket).toHaveBeenCalledWith('b1', 'Trip', 'Weekend gear'));
		await waitFor(() =>
			expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Trip')
		);
	});

	it('should show the cheapest split under the table when the basket has one', () => {
		renderPage(
			basket({
				split: {
					currency: 'EUR',
					lines: [
						{ productId: 'p1', host: 'shop-a.es', price: 100 },
						{ productId: 'p2', host: 'shop-b.es', price: 40 }
					],
					shipping: [
						{ host: 'shop-a.es', shipping: 5 },
						{ host: 'shop-b.es', shipping: 3 }
					],
					total: 148,
					complete: true
				}
			})
		);

		expect(screen.getByTestId('split-difference')).toHaveTextContent('€7.00 cheaper');
	});
});
