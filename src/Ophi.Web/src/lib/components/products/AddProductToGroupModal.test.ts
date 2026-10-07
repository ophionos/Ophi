import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/svelte';
import AddProductToGroupModal from './AddProductToGroupModal.svelte';
import { api, type GetProductsParams, type Product } from '$lib/api/client';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return { ...actual, api: { getProducts: vi.fn() } };
});

function makeProduct(n: number): Product {
	return {
		id: `prod-${n}`,
		name: `Product ${n}`,
		url: `https://example.com/${n}`,
		currentPrice: n,
		currency: 'USD',
		status: 'active',
		isFavourite: false,
		isOutOfStock: false,
		storeCount: 1,
		alertCount: 0,
		tags: [],
		customFields: []
	};
}

// 15 products so pagination kicks in at pageSize 10.
const allProducts = Array.from({ length: 15 }, (_, i) => makeProduct(i + 1));

function defaultProps(overrides: Partial<Record<string, unknown>> = {}) {
	return {
		isOpen: true,
		existingProductIds: [] as string[],
		onClose: vi.fn(),
		onAdd: vi.fn().mockResolvedValue(undefined),
		...overrides
	};
}

describe('AddProductToGroupModal', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		// Mirror the server: filter by search, then slice by page.
		vi.mocked(api.getProducts).mockImplementation(
			async (params: GetProductsParams = {}) => {
				const { search, page = 1, pageSize = 10 } = params;
				const filtered = search
					? allProducts.filter((p) => p.name.toLowerCase().includes(String(search).toLowerCase()))
					: allProducts;
				const start = (page - 1) * pageSize;
				return {
					items: filtered.slice(start, start + pageSize),
					total: filtered.length,
					page,
					pageSize,
					atLowestCount: 0,
					priceDropCount: 0,
					withAlertsCount: 0
				};
			}
		);
	});

	it('should render the "Add Products" modal and load products when open', async () => {
		render(AddProductToGroupModal, { props: defaultProps() });
		expect(screen.getByText('Add Products')).toBeInTheDocument();
		expect(await screen.findByText('Product 1')).toBeInTheDocument();
		expect(api.getProducts).toHaveBeenCalledWith({ search: undefined, page: 1, pageSize: 10 });
	});

	it('should not render when closed', () => {
		render(AddProductToGroupModal, { props: defaultProps({ isOpen: false }) });
		expect(screen.queryByText('Add Products')).not.toBeInTheDocument();
	});

	it('should select multiple products and call onAdd once with all selected ids', async () => {
		const onAdd = vi.fn().mockResolvedValue(undefined);
		render(AddProductToGroupModal, { props: defaultProps({ onAdd }) });

		await fireEvent.click(await screen.findByText('Product 1'));
		await fireEvent.click(screen.getByText('Product 2'));

		const addButton = screen.getByRole('button', { name: /^Add \(2\)$/ });
		expect(addButton).not.toBeDisabled();
		await fireEvent.click(addButton);

		await waitFor(() => {
			expect(onAdd).toHaveBeenCalledTimes(1);
			expect(onAdd).toHaveBeenCalledWith(['prod-1', 'prod-2']);
		});
	});

	it('should disable Add when nothing is selected', async () => {
		render(AddProductToGroupModal, { props: defaultProps() });
		await screen.findByText('Product 1');
		expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();
	});

	it('should mark products already in the group as disabled and non-selectable', async () => {
		render(AddProductToGroupModal, { props: defaultProps({ existingProductIds: ['prod-1'] }) });

		await screen.findByText('Product 1');
		expect(screen.getByText('Already in group')).toBeInTheDocument();

		// Clicking the already-in-group row does not select it.
		await fireEvent.click(screen.getByText('Product 1'));
		expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();
	});

	it('should keep selections sticky even when filtered out by search', async () => {
		render(AddProductToGroupModal, { props: defaultProps() });

		// Select Product 1 (on the first page).
		await fireEvent.click(await screen.findByText('Product 1'));
		expect(screen.getByRole('button', { name: /^Add \(1\)$/ })).toBeInTheDocument();

		// Search for "15" — server returns only Product 15, hiding Product 1 from the results list.
		await fireEvent.input(screen.getByLabelText('Search products'), { target: { value: '15' } });

		await waitFor(() => {
			expect(api.getProducts).toHaveBeenCalledWith({ search: '15', page: 1, pageSize: 10 });
		});
		await screen.findByText('Product 15');

		// Product 1 is gone from the result list but still present (sticky) in the Selected section.
		const selectedSection = screen.getByTestId('selected-section');
		expect(within(selectedSection).getByText('Product 1')).toBeInTheDocument();
		expect(screen.getByRole('button', { name: /^Add \(1\)$/ })).toBeInTheDocument();
	});

	it('should paginate via the server when there are more products than one page', async () => {
		render(AddProductToGroupModal, { props: defaultProps() });

		await screen.findByText('Product 1');
		// Product 11 is on page 2.
		expect(screen.queryByText('Product 11')).not.toBeInTheDocument();

		await fireEvent.click(screen.getByTestId('pagination-next'));

		await waitFor(() => {
			expect(api.getProducts).toHaveBeenCalledWith({ search: undefined, page: 2, pageSize: 10 });
		});
		expect(await screen.findByText('Product 11')).toBeInTheDocument();
	});

	it('should call onClose when Cancel is clicked', async () => {
		const onClose = vi.fn();
		render(AddProductToGroupModal, { props: defaultProps({ onClose }) });

		await screen.findByText('Product 1');
		await fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
		expect(onClose).toHaveBeenCalled();
	});
});
