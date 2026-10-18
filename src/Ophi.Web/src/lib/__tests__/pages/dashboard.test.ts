import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent, within } from '@testing-library/svelte';
import DashboardPage from '../../../routes/dashboard/+page.svelte';
import { page } from '$app/state';
import { replaceState } from '$app/navigation';
import { api } from '$lib/api/client';
import type { Product, ProductListResponse } from '$lib/api/client';
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
			updateProduct: vi.fn(),
			retryScrapeProductUrl: vi.fn()
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

const emptyPage: ProductListResponse = {
	items: [],
	total: 0,
	page: 1,
	pageSize: 24,
	atLowestCount: 0,
	priceDropCount: 0,
	withAlertsCount: 0,
	needsActionCount: 0,
	nearTargetCount: 0,
	productCount: 0,
	urlCount: 0,
	stores: []
};

function renderDashboard(
	url = 'http://localhost/dashboard',
	products: Partial<typeof emptyPage> = {},
	extra: { actionProducts?: ProductListResponse; challengeAvailable?: boolean } = {}
) {
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
					needsActionCount: 0,
					nearTargetCount: 0,
					productCount: 0,
					urlCount: 0,
					stores: [],
					...products
				},
				tags: [],
				actionProducts: emptyPage,
				challengeAvailable: false,
				...extra
			} as unknown as PageData
		}
	});
}

beforeEach(() => {
	vi.clearAllMocks();
	vi.mocked(api.getProducts).mockResolvedValue(emptyPage);
});

describe('Dashboard Page — header', () => {
	it('should render the page title as the level-one heading', () => {
		renderDashboard();
		expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Watchlist');
	});

	it('should count the products and URLs of the whole account', () => {
		renderDashboard('http://localhost/dashboard', { productCount: 12, urlCount: 19 });
		expect(screen.getByTestId('page-header')).toHaveTextContent('12 products · 19 URLs');
	});
});

describe('Dashboard Page — delete', () => {
	beforeEach(() => {
		vi.mocked(api.deleteProduct).mockResolvedValue(undefined as unknown as void);
	});

	it('should remove the product and re-pull from the server after delete', async () => {
		renderDashboard();

		expect(screen.getAllByText('Test Headphones').length).toBeGreaterThan(0);

		await fireEvent.click(screen.getAllByRole('button', { name: 'Actions for Test Headphones' })[0]);
		await fireEvent.click(screen.getByRole('menuitem', { name: /Delete/ }));
		await fireEvent.click(screen.getByRole('button', { name: /^Delete$/ }));

		await waitFor(() => {
			expect(screen.queryAllByText('Test Headphones')).toHaveLength(0);
		});

		// The reconciliation refresh is what defeats the in-flight-poll race and refreshes
		// the stats counts — without it the deleted product can reappear.
		expect(api.deleteProduct).toHaveBeenCalledWith('product-123');
		expect(api.getProducts).toHaveBeenCalled();
	});
});

describe('Dashboard Page — server-side filters', () => {
	it('should refetch with atLowest when the 90-day low chip is clicked', async () => {
		renderDashboard();

		await fireEvent.click(screen.getByTestId('opportunity-lowest'));

		await waitFor(() => {
			expect(api.getProducts).toHaveBeenCalledWith(expect.objectContaining({ atLowest: true }));
		});
	});

	it('should refetch with nearTarget when the near-target chip is clicked', async () => {
		renderDashboard('http://localhost/dashboard', { nearTargetCount: 2 });

		await fireEvent.click(screen.getByTestId('opportunity-near'));

		await waitFor(() => {
			expect(api.getProducts).toHaveBeenCalledWith(expect.objectContaining({ nearTarget: true }));
		});
	});

	it('should refetch with favourite when the Favourites toggle is clicked', async () => {
		renderDashboard();

		await fireEvent.click(screen.getByTestId('favourite-filter'));

		await waitFor(() => {
			expect(api.getProducts).toHaveBeenCalledWith(expect.objectContaining({ favourite: true }));
		});
	});

	it('should refetch with storeId when a store is chosen', async () => {
		renderDashboard('http://localhost/dashboard', {
			stores: [
				{ storeId: 'amazon', name: 'Amazon' },
				{ storeId: 'shop', name: 'The Shop' }
			]
		});

		await fireEvent.change(screen.getByLabelText('Store'), { target: { value: 'shop' } });

		await waitFor(() => {
			expect(api.getProducts).toHaveBeenCalledWith(expect.objectContaining({ storeId: 'shop' }));
		});
	});

	it('should ask for the sparkline on every refetch, whatever the view', async () => {
		renderDashboard();

		await fireEvent.change(screen.getByLabelText('Status'), { target: { value: 'paused' } });

		await waitFor(() => {
			expect(api.getProducts).toHaveBeenCalledWith(
				expect.objectContaining({ status: 'paused', includeSparkline: true })
			);
		});
	});

	it('should show account-wide counts from the response, not per-page tallies', () => {
		// One product on the page, but the server reports 4 at their low and 3 near a target.
		renderDashboard('http://localhost/dashboard', { atLowestCount: 4, nearTargetCount: 3 });

		expect(screen.getByTestId('opportunity-lowest')).toHaveTextContent('4 at their 90-day low');
		expect(screen.getByTestId('opportunity-near')).toHaveTextContent('3 alerts near target');
	});
});

describe('Dashboard Page — active filters', () => {
	it('should show each filter in the URL as a removable chip', () => {
		renderDashboard('http://localhost/dashboard?status=paused&filter=lowest');

		expect(screen.getByTestId('active-filter-status')).toHaveTextContent('Status: Paused');
		expect(screen.getByTestId('active-filter-opportunity')).toHaveTextContent('At their 90-day low');
	});

	it('should remove one filter from its chip', async () => {
		renderDashboard('http://localhost/dashboard?status=paused&favourite=true');

		await fireEvent.click(screen.getByRole('button', { name: 'Remove filter: Status: Paused' }));

		await waitFor(() => {
			expect(api.getProducts).toHaveBeenCalledWith(expect.objectContaining({ status: undefined, favourite: true }));
		});
		expect(screen.queryByTestId('active-filter-status')).not.toBeInTheDocument();
	});

	it('should clear every filter at once', async () => {
		renderDashboard('http://localhost/dashboard?status=paused&favourite=true&filter=lowest');

		await fireEvent.click(screen.getByTestId('clear-all-filters'));

		await waitFor(() => {
			expect(api.getProducts).toHaveBeenCalledWith(
				expect.objectContaining({ status: undefined, favourite: undefined, atLowest: undefined })
			);
		});
		expect(screen.queryByTestId('active-filters')).not.toBeInTheDocument();
	});

	it('should name the active filters when nothing matches and offer to clear them', async () => {
		renderDashboard('http://localhost/dashboard?status=error', { items: [], total: 0, productCount: 3 });

		expect(screen.getByTestId('empty-filter-result')).toHaveTextContent('No products match: Status: Error');

		await fireEvent.click(screen.getByTestId('empty-clear-filters'));

		await waitFor(() => {
			expect(api.getProducts).toHaveBeenCalledWith(expect.objectContaining({ status: undefined }));
		});
	});
});

describe('Dashboard Page — needs your action', () => {
	const blocked: Product = {
		...mockProduct,
		id: 'product-blocked',
		name: 'Blocked Speaker',
		attention: { kind: 'blocked', isAction: true, productUrlId: 'url-9', url: 'https://shop.example/x', consecutiveFailures: 4 }
	};
	const actionPage: ProductListResponse = { ...emptyPage, items: [blocked], total: 1 };

	it('should hide the action card when nothing needs the user', () => {
		renderDashboard();
		expect(screen.queryByTestId('needs-action-card')).not.toBeInTheDocument();
	});

	it('should list the products that need action from the loader', () => {
		renderDashboard('http://localhost/dashboard', { needsActionCount: 1 }, { actionProducts: actionPage });

		const card = screen.getByTestId('needs-action-card');
		expect(card).toHaveTextContent('Needs your action · 1');
		expect(within(card).getByText('Blocked Speaker')).toBeInTheDocument();
	});

	it('should retry the URL to act on and re-pull when Retry now is clicked', async () => {
		vi.mocked(api.retryScrapeProductUrl).mockResolvedValue(undefined as never);
		renderDashboard('http://localhost/dashboard', { needsActionCount: 1 }, { actionProducts: actionPage });

		await fireEvent.click(within(screen.getByTestId('needs-action-card')).getByRole('button', { name: 'Retry now' }));

		await waitFor(() => {
			expect(api.retryScrapeProductUrl).toHaveBeenCalledWith('product-blocked', 'url-9');
		});
		expect(api.getProducts).toHaveBeenCalledWith(expect.objectContaining({ needsAction: true }));
	});

	it('should filter the table to the action rows when Show in table is clicked', async () => {
		vi.mocked(api.getProducts).mockResolvedValue({ ...actionPage, productCount: 3, needsActionCount: 1 });
		renderDashboard('http://localhost/dashboard', { needsActionCount: 1 }, { actionProducts: actionPage });

		await fireEvent.click(screen.getByTestId('needs-action-show'));

		await waitFor(() => {
			expect(api.getProducts).toHaveBeenCalledWith(expect.objectContaining({ needsAction: true, page: 1 }));
		});
		const url = vi.mocked(replaceState).mock.calls.at(-1)![0] as URL;
		expect(url.searchParams.get('filter')).toBe('action');
		expect(screen.getByTestId('active-filter-opportunity')).toHaveTextContent('Needs your action');
	});

	it('should restore the action filter from the URL', async () => {
		renderDashboard('http://localhost/dashboard?filter=action', { needsActionCount: 1 }, { actionProducts: actionPage });
		expect(screen.getByTestId('active-filter-opportunity')).toHaveTextContent('Needs your action');
	});
});

describe('Dashboard Page — empty account', () => {
	it('should show only the URL field and the empty state', () => {
		renderDashboard('http://localhost/dashboard', { items: [], total: 0, productCount: 0 });

		expect(screen.getByText('Start tracking prices')).toBeInTheDocument();
		expect(screen.queryByTestId('filter-bar')).not.toBeInTheDocument();
		expect(screen.queryByTestId('opportunity-chips')).not.toBeInTheDocument();
	});
});

describe('Dashboard Page — bulk actions', () => {
	async function enterSelectionAndPick() {
		renderDashboard();
		await fireEvent.click(screen.getByTestId('bulk-select-toggle'));
		await fireEvent.click(screen.getAllByLabelText('Select Test Headphones')[0]);
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
		expect(screen.queryAllByLabelText('Select Test Headphones')).toHaveLength(0);
	});
});

describe('Dashboard Page — URL state', () => {
	it('should initialize filters from the URL', () => {
		renderDashboard('http://localhost/dashboard?search=headphones&status=paused');

		expect(screen.getByTestId('search-input')).toHaveValue('headphones');
	});

	it('should mirror an opportunity filter into the URL via shallow replaceState', async () => {
		renderDashboard();

		await fireEvent.click(screen.getByTestId('opportunity-lowest'));

		await waitFor(() => {
			expect(replaceState).toHaveBeenCalled();
		});
		const url = vi.mocked(replaceState).mock.calls.at(-1)![0] as URL;
		expect(url.toString()).toContain('filter=lowest');
	});

	it('should mirror the store filter into the URL', async () => {
		renderDashboard('http://localhost/dashboard', {
			stores: [
				{ storeId: 'amazon', name: 'Amazon' },
				{ storeId: 'shop', name: 'The Shop' }
			]
		});

		await fireEvent.change(screen.getByLabelText('Store'), { target: { value: 'shop' } });

		const url = vi.mocked(replaceState).mock.calls.at(-1)![0] as URL;
		expect(url.searchParams.get('store')).toBe('shop');
	});

	it('should drop cleared filters from the URL', async () => {
		renderDashboard('http://localhost/dashboard?filter=lowest');

		// Clicking the active chip clears the filter.
		await fireEvent.click(screen.getByTestId('opportunity-lowest'));

		await waitFor(() => {
			expect(replaceState).toHaveBeenCalled();
		});
		const url = vi.mocked(replaceState).mock.calls.at(-1)![0] as URL;
		expect(url.toString()).not.toContain('filter=');
	});
});
