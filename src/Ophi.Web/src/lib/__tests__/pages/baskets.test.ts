import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/svelte';
import BasketsPage from '../../../routes/baskets/+page.svelte';
import { api, type BasketSummary } from '$lib/api/client';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return { ...actual, api: { createBasket: vi.fn() } };
});

vi.mock('$app/navigation', () => ({ goto: vi.fn() }));

function summary(overrides: Partial<BasketSummary> = {}): BasketSummary {
	return {
		id: 'b1',
		name: 'Camping',
		description: null,
		itemCount: 3,
		updatedAt: '2026-01-01T00:00:00Z',
		bestStore: null,
		bestTotal: null,
		bestCurrency: null,
		...overrides
	};
}

function renderPage(baskets: BasketSummary[]) {
	return render(BasketsPage, {
		props: { data: { user: null, authChecked: true, isPublic: false, baskets } } as never
	});
}

describe('Baskets page', () => {
	beforeEach(() => {
		vi.clearAllMocks();
	});

	it('should show the best store and its total when a basket has a best store', () => {
		renderPage([summary({ bestStore: 'shop-a.es', bestTotal: 28, bestCurrency: 'EUR' })]);

		const card = screen.getByTestId('basket-card');
		expect(card).toHaveTextContent('Camping');
		expect(card).toHaveTextContent('3 items');
		expect(card).toHaveTextContent('Best: shop-a.es · €28.00');
	});

	it('should say no complete store yet when a basket has no best store', () => {
		renderPage([summary()]);

		expect(screen.getByTestId('basket-card')).toHaveTextContent('No complete store yet');
	});

	it('should link each card to the basket page', () => {
		renderPage([summary()]);

		expect(screen.getByRole('link', { name: 'Camping' })).toHaveAttribute('href', '/baskets/b1');
	});

	it('should explain what a basket is when there are no baskets', () => {
		renderPage([]);

		expect(screen.getByText(/Products you buy together, with a total per store/)).toBeInTheDocument();
	});

	it('should create the basket and list it when the modal is submitted', async () => {
		vi.mocked(api.createBasket).mockResolvedValue({
			id: 'b2',
			name: 'Kitchen',
			description: null,
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z',
			items: [],
			stores: [],
			bestStore: null,
			split: null
		});
		renderPage([]);

		await fireEvent.click(screen.getAllByRole('button', { name: /New basket/ })[0]);
		const dialog = await screen.findByRole('dialog', { name: 'New basket' });
		await fireEvent.input(within(dialog).getByLabelText(/Name/), { target: { value: 'Kitchen' } });
		await fireEvent.click(within(dialog).getByRole('button', { name: 'Create' }));

		await waitFor(() => expect(api.createBasket).toHaveBeenCalledWith('Kitchen', undefined));
		expect(await screen.findByRole('link', { name: 'Kitchen' })).toBeInTheDocument();
	});

	it('should show the server error in the modal when creating fails', async () => {
		vi.mocked(api.createBasket).mockRejectedValue(new Error('Name is too long'));
		renderPage([]);

		await fireEvent.click(screen.getAllByRole('button', { name: /New basket/ })[0]);
		const dialog = await screen.findByRole('dialog', { name: 'New basket' });
		await fireEvent.input(within(dialog).getByLabelText(/Name/), { target: { value: 'x' } });
		await fireEvent.click(within(dialog).getByRole('button', { name: 'Create' }));

		expect(await within(dialog).findByText('Name is too long')).toBeInTheDocument();
	});
});
