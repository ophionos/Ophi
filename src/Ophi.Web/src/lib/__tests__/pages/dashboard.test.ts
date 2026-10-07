import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent, within } from '@testing-library/svelte';
import DashboardPage from '../../../routes/dashboard/+page.svelte';
import { page } from '$app/state';
import { replaceState } from '$app/navigation';
import { api } from '$lib/api/client';
import type { Product } from '$lib/api/client';
import type { PageData } from '../../../routes/dashboard/$types';

vi.mock('$lib/api/client', async () => {
	const actual = await vi.importActual('$lib/api/client');
	return {
		...actual,
		api: {
			getProducts: vi.fn(),
			getTags: vi.fn(),
			deleteProduct: vi.fn(),
			addProduct: vi.fn(),
			createProduct: vi.fn(),
			updateProduct: vi.fn()
		}
	};
});

const mockProduct: Product = {
	id: 'product-123',
	name: 'Test Headphones',
	url: 'https://example.com/product',
	imageUrl: 'https://example.com/image.jpg',
	currentPrice: 99.99,
	previousPrice: 109.99,
	priceChange: -9.1,
	currency: 'USD',
	lastChecked: '2024-01-15T12:00:00Z',
	status: 'active',
	isFavourite: false,
	isOutOfStock: false,
	storeCount: 1,
	alertCount: 0,
	tags: [],
	customFields: []
};

const emptyPage = {
	items: [],
	total: 0,
	page: 1,
	pageSize: 24,
	atLowestCount: 0,
	priceDropCount: 0,
	withAlertsCount: 0
};

function renderDashboard(url = 'http://localhost/dashboard', products: Partial<typeof emptyPage> = {}) {
	page.url = new URL(url) as unknown as typeof page.url;
	return render(DashboardPage, {
		props: {
			data: {
				user: { id: '1', email: 'a@b.com', name: 'A' },
				products: {
					items: [mockProduct],
					total: 1,
					page: 1,
					pageSize: 24,
					atLowestCount: 1,
					priceDropCount: 1,
					withAlertsCount: 0,
					...products
				},
				tags: []
			} as unknown as PageData
		}
	});
}

beforeEach(() => {
	vi.clearAllMocks();
	vi.mocked(api.getProducts).mockResolvedValue(emptyPage);
});

describe('Dashboard Page — delete', () => {
	beforeEach(() => {
		vi.mocked(api.deleteProduct).mockResolvedValue(undefined as unknown as void);
	});

	it('should remove the product and re-pull from the server after delete', async () => {
		renderDashboard();

		expect(screen.getByText('Test Headphones')).toBeInTheDocument();

		await fireEvent.click(screen.getByRole('button', { name: 'Delete product' }));
		await fireEvent.click(screen.getByRole('button', { name: /^Delete$/ }));

		await waitFor(() => {
			expect(screen.queryByText('Test Headphones')).not.toBeInTheDocument();
		});

		// The reconciliation refresh is what defeats the in-flight-poll race and refreshes
		// the stats counts — without it the deleted product can reappear.
		expect(api.deleteProduct).toHaveBeenCalledWith('product-123');
		expect(api.getProducts).toHaveBeenCalled();
	});
});

describe('Dashboard Page — server-side filters', () => {
	it('should refetch with priceDrop when the Drops stat is clicked', async () => {
		renderDashboard();

		await fireEvent.click(screen.getByText('Drops'));

		await waitFor(() => {
			expect(api.getProducts).toHaveBeenCalledWith(
				expect.objectContaining({ priceDrop: true })
			);
		});
	});

	it('should refetch with hasAlerts when the Alerts stat is clicked', async () => {
		renderDashboard();

		await fireEvent.click(screen.getByText('Alerts'));

		await waitFor(() => {
			expect(api.getProducts).toHaveBeenCalledWith(
				expect.objectContaining({ hasAlerts: true })
			);
		});
	});

	it('should refetch with favourite when the Favourites chip is clicked', async () => {
		renderDashboard();

		await fireEvent.click(screen.getByTestId('favourite-filter'));

		await waitFor(() => {
			expect(api.getProducts).toHaveBeenCalledWith(
				expect.objectContaining({ favourite: true })
			);
		});
	});

	it('should show global counts from the response, not per-page tallies', () => {
		// One product on the page, but the server reports 5 drops / 3 with alerts globally.
		renderDashboard('http://localhost/dashboard', { priceDropCount: 5, withAlertsCount: 3 });

		const statsBar = within(screen.getByTestId('stats-bar'));
		expect(statsBar.getByText('5')).toBeInTheDocument();
		expect(statsBar.getByText('3')).toBeInTheDocument();
	});
});

describe('Dashboard Page — bulk actions', () => {
	async function enterSelectionAndPick() {
		renderDashboard();
		await fireEvent.click(screen.getByTestId('bulk-select-toggle'));
		await fireEvent.click(screen.getByLabelText('Select Test Headphones'));
	}

	it('should show the bulk bar with a count once a product is selected', async () => {
		await enterSelectionAndPick();

		expect(screen.getByText('1 selected')).toBeInTheDocument();
	});

	it('should pause every selected product and re-pull from the server', async () => {
		vi.mocked(api.updateProduct).mockResolvedValue(mockProduct);
		await enterSelectionAndPick();

		await fireEvent.click(screen.getByTestId('bulk-pause'));

		await waitFor(() => {
			expect(api.updateProduct).toHaveBeenCalledWith('product-123', { status: 'paused' });
		});
		expect(api.getProducts).toHaveBeenCalled();
	});

	it('should resume every selected product', async () => {
		vi.mocked(api.updateProduct).mockResolvedValue(mockProduct);
		await enterSelectionAndPick();

		await fireEvent.click(screen.getByTestId('bulk-resume'));

		await waitFor(() => {
			expect(api.updateProduct).toHaveBeenCalledWith('product-123', { status: 'active' });
		});
	});

	it('should delete selected products only after confirmation', async () => {
		vi.mocked(api.deleteProduct).mockResolvedValue(undefined as unknown as void);
		await enterSelectionAndPick();

		await fireEvent.click(screen.getByTestId('bulk-delete'));
		expect(api.deleteProduct).not.toHaveBeenCalled();

		const dialog = await screen.findByRole('dialog', { name: /delete 1 product/i });
		await fireEvent.click(within(dialog).getByRole('button', { name: /^Delete$/ }));

		await waitFor(() => {
			expect(api.deleteProduct).toHaveBeenCalledWith('product-123');
		});
	});

	it('should select the whole page at once', async () => {
		renderDashboard();
		await fireEvent.click(screen.getByTestId('bulk-select-toggle'));

		await fireEvent.click(screen.getByTestId('bulk-select-page'));

		expect(screen.getByText('1 selected')).toBeInTheDocument();
	});

	it('should clear the selection when leaving selection mode', async () => {
		await enterSelectionAndPick();

		await fireEvent.click(screen.getByTestId('bulk-select-toggle'));

		expect(screen.queryByText('1 selected')).not.toBeInTheDocument();
		expect(screen.queryByLabelText('Select Test Headphones')).not.toBeInTheDocument();
	});
});

describe('Dashboard Page — URL state', () => {
	it('should initialize filters from the URL', () => {
		renderDashboard('http://localhost/dashboard?search=headphones&status=paused');

		expect(screen.getByTestId('search-input')).toHaveValue('headphones');
	});

	it('should mirror a stats filter into the URL via shallow replaceState', async () => {
		renderDashboard();

		await fireEvent.click(screen.getByText('Drops'));

		await waitFor(() => {
			expect(replaceState).toHaveBeenCalled();
		});
		const url = vi.mocked(replaceState).mock.calls.at(-1)![0] as URL;
		expect(url.toString()).toContain('filter=drops');
	});

	it('should drop cleared filters from the URL', async () => {
		renderDashboard('http://localhost/dashboard?filter=drops');

		// Clicking the active Drops stat clears the filter.
		await fireEvent.click(screen.getByText('Drops'));

		await waitFor(() => {
			expect(replaceState).toHaveBeenCalled();
		});
		const url = vi.mocked(replaceState).mock.calls.at(-1)![0] as URL;
		expect(url.toString()).not.toContain('filter=');
	});
});
