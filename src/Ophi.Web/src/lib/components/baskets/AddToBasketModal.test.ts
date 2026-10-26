import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import AddToBasketModal from './AddToBasketModal.svelte';
import { api, ApiError, type Basket, type BasketSummary } from '$lib/api/client';
import { toast } from '$lib/stores/toast.svelte';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: { listBaskets: vi.fn(), createBasket: vi.fn(), addBasketItems: vi.fn() }
	};
});

function summary(id: string, name: string): BasketSummary {
	return {
		id,
		name,
		description: null,
		itemCount: 1,
		updatedAt: '2026-01-01T00:00:00Z',
		bestStore: null,
		bestTotal: null,
		bestCurrency: null
	};
}

function fullBasket(id: string, name: string): Basket {
	return {
		id,
		name,
		description: null,
		createdAt: '2026-01-01T00:00:00Z',
		updatedAt: '2026-01-01T00:00:00Z',
		items: [],
		stores: [],
		bestStore: null,
		split: null
	};
}

function props(overrides: Record<string, unknown> = {}) {
	return { open: true, productIds: ['p1', 'p2'], onClose: vi.fn(), onAdded: vi.fn(), ...overrides };
}

describe('AddToBasketModal', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		vi.mocked(api.listBaskets).mockResolvedValue({
			items: [summary('b1', 'Camping'), summary('b2', 'Kitchen')]
		});
	});

	it('should list the user baskets when it opens', async () => {
		render(AddToBasketModal, { props: props() });

		expect(await screen.findByLabelText('Camping')).toBeInTheDocument();
		expect(screen.getByLabelText('Kitchen')).toBeInTheDocument();
		expect(screen.getByLabelText('New basket')).toBeInTheDocument();
	});

	it('should not load baskets when it is closed', () => {
		render(AddToBasketModal, { props: props({ open: false }) });

		expect(api.listBaskets).not.toHaveBeenCalled();
	});

	it('should add the products to the chosen basket when an existing basket is confirmed', async () => {
		const added = fullBasket('b2', 'Kitchen');
		vi.mocked(api.addBasketItems).mockResolvedValue(added);
		const p = props();
		render(AddToBasketModal, { props: p });

		await fireEvent.click(await screen.findByLabelText('Kitchen'));
		await fireEvent.click(screen.getByRole('button', { name: 'Add to basket' }));

		await waitFor(() => {
			expect(api.addBasketItems).toHaveBeenCalledWith('b2', { productIds: ['p1', 'p2'] });
		});
		expect(api.createBasket).not.toHaveBeenCalled();
		expect(p.onAdded).toHaveBeenCalledWith(added);
		expect(p.onClose).toHaveBeenCalled();
		expect(toast.items.map((t) => t.message)).toContain('Added to Kitchen');
	});

	it('should create the basket and then add the products when a new basket is confirmed', async () => {
		vi.mocked(api.createBasket).mockResolvedValue(fullBasket('b9', 'Trip'));
		vi.mocked(api.addBasketItems).mockResolvedValue(fullBasket('b9', 'Trip'));
		render(AddToBasketModal, { props: props() });

		await fireEvent.click(await screen.findByLabelText('New basket'));
		await fireEvent.input(screen.getByLabelText('Basket name'), { target: { value: 'Trip' } });
		await fireEvent.click(screen.getByRole('button', { name: 'Add to basket' }));

		await waitFor(() => {
			expect(api.addBasketItems).toHaveBeenCalledWith('b9', { productIds: ['p1', 'p2'] });
		});
		expect(api.createBasket).toHaveBeenCalledWith('Trip');
	});

	it('should ask for a name when a new basket has none', async () => {
		render(AddToBasketModal, { props: props() });

		await fireEvent.click(await screen.findByLabelText('New basket'));
		await fireEvent.click(screen.getByRole('button', { name: 'Add to basket' }));

		expect(await screen.findByText('Name is required')).toBeInTheDocument();
		expect(api.createBasket).not.toHaveBeenCalled();
	});

	it('should show the server error inline when adding fails', async () => {
		vi.mocked(api.addBasketItems).mockRejectedValue(new ApiError('NOT_FOUND', 'Product not found'));
		const p = props();
		render(AddToBasketModal, { props: p });

		await fireEvent.click(await screen.findByLabelText('Camping'));
		await fireEvent.click(screen.getByRole('button', { name: 'Add to basket' }));

		expect(await screen.findByText('Product not found')).toBeInTheDocument();
		expect(p.onClose).not.toHaveBeenCalled();
	});

	it('should preselect a new basket when the user has none', async () => {
		vi.mocked(api.listBaskets).mockResolvedValue({ items: [] });
		render(AddToBasketModal, { props: props() });

		expect(await screen.findByLabelText('Basket name')).toBeInTheDocument();
	});
});
